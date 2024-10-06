using System;
using System.Buffers.Binary;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using Unity.VisualScripting.FullSerializer;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using UnityEngine.XR;

public class RecvBuffer
{
    enum ParseStage
    {
        TLEN, DATA
    }

    static readonly int TLEN_SIZE = 5;
    byte[] _tlenBytes = new byte[TLEN_SIZE];
    int _tlenBytesPosition = 0;

    ParseStage _parseStage = ParseStage.TLEN;
    int _needBytes = 1;

    public byte[] buffer = null;
    public int position = 0;

    public byte[][] Recv(byte[] bytes, int n)
    {
        int bindex = 0;

        List<byte[]> msgs = new List<byte[]>();

        while (_needBytes > 0 && bindex < n)
        {
            switch (_parseStage)
            {
                case ParseStage.TLEN:
                    int index = TLEN_SIZE - _needBytes;
                    _tlenBytes[_tlenBytesPosition++] = bytes[bindex];
                    if ((bytes[bindex] & 0x80) == 0)
                        _needBytes = 0;

                    bindex += 1;
                    if (_needBytes == 0)
                    {
                        _parseStage = ParseStage.DATA;
                        _needBytes = CalcPackageDataLength();
                        _tlenBytesPosition = 0;

                        buffer = new byte[_needBytes];
                    }
                    break;
                case ParseStage.DATA:
                    int leftBytesNum = n - bindex;
                    if (leftBytesNum < _needBytes)
                    {
                        Buffer.BlockCopy(bytes, bindex, buffer, position, leftBytesNum);
                        _needBytes -= leftBytesNum;
                        bindex += leftBytesNum;
                        position += leftBytesNum;
                    }
                    else
                    {
                        Buffer.BlockCopy(bytes, bindex, buffer, position, _needBytes);
                        bindex += _needBytes;
                        position = 0;

                        // finish one msg
                        byte[] msg = new byte[_needBytes];
                        Buffer.BlockCopy(buffer, 0, msg, 0, _needBytes);
                        msgs.Add(msg);

                        // reset to initial state
                        _parseStage = ParseStage.TLEN;
                        _needBytes = 1;
                    }
                    break;
            }
        }

        return msgs.ToArray();
    }

    int Read7BitEncodedInt(byte[] bytes)
    {
        int value = 0;

        byte c;
        int i = 0;
        int s = 0;
        do
        {
            c = bytes[i++];
            int x = (c & 0x7F);
            x <<= s;
            value += x;
            s += 7;
        } while ((c & 0x80) != 0);

        return value;
    }

    int CalcPackageDataLength()
    {
        return Read7BitEncodedInt(_tlenBytes);
    }
}

class SendBuffer
{
    public byte[] _buffer;
    public uint _capacity;
    // _buffer[_startPos, _endPos] 为未被发送的数据
    public uint _startPos, _endPos;
    public uint _sendBytes = 0;

    public SendBuffer(uint len)
    {
        _buffer = new byte[len];
        _capacity = len;
        // _startPos由发送线程修改，_endPos由主线程修改，理论上不会有线程问题
        _startPos = _endPos = 0;
    }

    public bool Empty()
    {
        return _startPos == _endPos; 
    }

    public uint Length()
    {
        return _endPos >= _startPos ? _endPos - _startPos : _endPos + _capacity - _startPos;
    }

    public void Send(byte[] data)
    {
        uint dataLength = (uint)data.Length;
        if (dataLength + Length() > _capacity)
        {
            throw new OverflowException("SendBuffer overflow");
        }

        uint lastCapacity = _capacity - _endPos;
        uint copyLength = Math.Min(dataLength, lastCapacity);
        Buffer.BlockCopy(data, 0, _buffer, (int)_endPos, (int)copyLength);

        if (copyLength < dataLength)
        {
            Buffer.BlockCopy(data, (int)copyLength, _buffer, 0, (int)(dataLength - copyLength));
        }

        _endPos = (_endPos + dataLength) % _capacity;
    }

    public void Flush(NetworkStream stream)
    {
        if (_endPos == _startPos)
            return;

        uint saveEndPos = _endPos;
        if (_endPos > _startPos)
        {
            int len = (int)(_endPos - _startPos);
            stream.Write(_buffer, (int)_startPos, len);

            _sendBytes += (uint)len;
        }
        else
        {
            int len = (int)(_capacity - _startPos);
            stream.Write(_buffer, (int)_startPos, len);
            stream.Write(_buffer, 0, (int)_endPos);

            _sendBytes += (uint)len + _endPos;
        }

        _startPos = saveEndPos;
    }
}

public class NetworkManager : MonoBehaviour
{
    public static NetworkManager Instance;

    [Tooltip("connect timeout in millisecond")]
    public int ConnectTimeoutMs = 2000;

    private TcpClient _client;
    private NetworkStream _stream;

    private bool _isConnected = false;

    private uint _sendBufferSize = 64 * 1024;
    private SendBuffer _sendBuffer = null;
    private RecvBuffer _recvBuffer = null;

    private ConcurrentQueue<byte[]> _msgQueue = new ConcurrentQueue<byte[]>();
    private Dictionary<string, GameObject> _players = new Dictionary<string, GameObject>();

    void Awake()
    {
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    // Start is called before the first frame update
    void Start()
    {
    }

    // Update is called once per frame
    void Update()
    {
        HandleNetworkMsg();
    }

    private void HandleNetworkMsg()
    {
        while (true)
        {
            byte[] bytes = null;
            if (_msgQueue.TryDequeue(out bytes!))
            {
                string msg = Encoding.UTF8.GetString(bytes);
                // Debug.Log("Recv: " + msg);
                HandleNetworkMsg(msg);
            }
            else
            {
                break;
            }
        }
    }

    private void HandleNetworkMsg(string msg)
    {
        if (msg.StartsWith("login_reply#"))
        {
            int pos = msg.IndexOf('#');
            string tmp = msg.Substring(pos+1);
            int result = int.Parse(tmp);
            if (result == 0)
            {
                Debug.Log("login successed");    
                StartCoroutine(loadScene());
            }
            else
            {
                Debug.Log("login failed: " + result);    
            }
        }
        else if (msg.StartsWith("join_failed#"))
        {
            int pos = msg.IndexOf('#');
            string reason = msg.Substring(pos+1);
            Debug.Log("join space failed: " + reason);
        }
        else if (msg.StartsWith("join_successed#"))
        {
            int pos = msg.IndexOf('#');
            string tmp = msg.Substring(pos+1);
            string[] arguments = tmp.Split(':');

            Vector3 position = new Vector3(float.Parse(arguments[1]), float.Parse(arguments[2]), float.Parse(arguments[3]));
            Debug.Log($"join space successed! name: {arguments[0]}, position: {position}");

            GameObject prefab = Resources.Load<GameObject>("Character/MainCharacter");
            if (prefab != null) {
                GameObject.Instantiate(prefab, position, Quaternion.identity);
            } else {
                Debug.Log("main character not found");
            }
        }
        else if (msg.StartsWith("players_enter_sight#"))
        {
            int pos = msg.IndexOf('#');
            string tmp = msg.Substring(pos+1);
            if (tmp.Length == 0)
                return;

            string[] playerStrings = tmp.Split('|');
            foreach (string playerString in playerStrings)
            {
                string[] arguments = playerString.Split(':');
                Vector3 position =  new Vector3(float.Parse(arguments[1]), float.Parse(arguments[2]), float.Parse(arguments[3]));
                Debug.Log($"player enter sight, name: {arguments[0]}, position: {position}");
                
                GameObject otherPlayer = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                otherPlayer.transform.position = position;
                _players.Add(arguments[0], otherPlayer);
            }
        }
        else if (msg.StartsWith("players_leave_sight#"))
        {
            int pos = msg.IndexOf('#');
            string tmp = msg.Substring(pos+1);
            if (tmp.Length == 0)
                return;
            
            string[] playerStrings = tmp.Split('|');
            foreach (string name in playerStrings)
            {
                Debug.Log($"player leave sight, name: {name}");
                
                GameObject player = null;
                if (_players.Remove(name, out player)) {
                    GameObject.Destroy(player);
                }
            }
        }
    }

    public async Task<bool> ConnectAsync(string host, int port, int connectTimeoutMs)
    {
        _client = new TcpClient();
        try
        {
            Task connectTask = _client.ConnectAsync(host, port);
            Task delayTask = Task.Delay(connectTimeoutMs);
            if (await Task.WhenAny(connectTask, delayTask) == delayTask)
            {
                Debug.LogError("connect timeout");
                return false;
            }

            // it may failed immediately when connect to loopback interface
            if (connectTask.Status == TaskStatus.Faulted) {
                foreach (var ex in connectTask.Exception?.InnerExceptions ?? new(Array.Empty<Exception>()))
                {
                    Debug.Log(ex.ToString());
                }
                return false;
            }
        }
        catch (Exception e)
        {
            Debug.LogError(e.ToString());
            return false;
        }

        Debug.Log("connect to server success");
        _stream = _client.GetStream();

        _sendBuffer = new SendBuffer(_sendBufferSize);
        _recvBuffer = new RecvBuffer();
        return true;
    }

    public async void Connect(string host, int port, Action<bool> connectCallback)
    {
        Debug.Log("connecting to server");
        _isConnected = await ConnectAsync(host, port, ConnectTimeoutMs);
        connectCallback(_isConnected);

        if (_isConnected)
        {
            Task sendTask = Task.Run(() => { SendThreadFunc(); });
            Task readTask = Task.Run(() => { RecvThreadFunc(); });
            Task.WhenAll(sendTask, readTask);
        }
    }

    public void Close()
    {
        Debug.Log("close socket");
        _isConnected = false;
        _stream.Close();
        _stream = null;
    }

    public void Write7BitEncodedInt(BinaryWriter writer, int value)
    {
        uint uValue = (uint)value;

        // Write out an int 7 bits at a time. The high bit of the byte,
        // when on, tells reader to continue reading more bytes.
        //
        // Using the constants 0x7F and ~0x7F below offers smaller
        // codegen than using the constant 0x80.

        while (uValue > 0x7Fu)
        {
            writer.Write((byte)(uValue | ~0x7Fu));
            uValue >>= 7;
        }

        writer.Write((byte)uValue);
    }

    public void Send(string msg)
    {
        Debug.Log("Send: " + msg);
        using (MemoryStream mem = new MemoryStream())
        {
            using (BinaryWriter binaryWriter = new BinaryWriter(mem))
            {
                byte[] bytes = Encoding.UTF8.GetBytes(msg);
                Write7BitEncodedInt(binaryWriter, bytes.Length);
                binaryWriter.Write(bytes);
            }
            byte[] data = mem.ToArray();
            _sendBuffer.Send(data);
        }
    }

    private void SendThreadFunc()
    {
        while (_isConnected)
        {
            if (_sendBuffer.Empty())
            {
                Thread.Sleep(100);
            }
            else
            {
                try
                {
                    _sendBuffer.Flush(_stream);
                }
                catch (Exception e)
                {
                    Debug.LogError(e.ToString());
                    break;
                }
            }
        }

        if (_isConnected)
            OnLostServer();
    }

    private void RecvThreadFunc()
    {
        byte[] buffer = new byte[2048];
        while (_isConnected)
        {
            try
            {
                int nReads = _stream.Read(buffer, 0, buffer.Length);
                if (nReads == 0)
                    break;

                byte[][] msgs = _recvBuffer.Recv(buffer, nReads);

                foreach (byte[] bytes in msgs)
                {
                    _msgQueue.Enqueue(bytes);
                }
            }
            catch (Exception e)
            {
                Debug.LogError(e.ToString());
                break;
            }
        }

        if (_isConnected)
            OnLostServer();
    }

    private void OnLostServer()
    {
        Debug.LogError("OnLostServer");
        Close();
    }

    private IEnumerator loadScene() {
        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync("DemoScene");
        while (!asyncLoad.isDone)
        {
            yield return null;
        }

        string joinRequest = "join";
        Send(joinRequest);
    }
}
