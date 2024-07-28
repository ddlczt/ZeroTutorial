using System.Collections;
using System.Collections.Generic;
using System.IO.IsolatedStorage;
using UnityEngine;
using UnityEngine.XR;

public class PlayerController : MonoBehaviour
{
    [Header("Player")]
    [Tooltip("Move speed of the character in m/s")]
    public float MoveSpeed = 2.0f;

    [Tooltip("Sprint speed of the character in m/s")]
    public float SprintSpeed = 5.335f;

    [Tooltip("rotate speed")]
    public float rotSpeed = 0.8f;

    [Tooltip("Acceleration and deceleration")]
    public float SpeedChangeRate = 10.0f;

    [Tooltip("How fast the character turns to face movement direction")]
    [Range(0.0f, 0.3f)]
    public float RotationSmoothTime = 0.12f;

    [Space(10)]
    [Tooltip("The height the player can jump")]
    public float JumpHeight = 1.2f;

    [Tooltip("The character uses its own gravity value. The engine default is -9.81f")]
    public float Gravity = -15.0f;

    [Space(10)]
    [Tooltip("Time required to pass before being able to jump again. Set to 0f to instantly jump again")]
    public float JumpTimeout = 0.50f;

    [Tooltip("Time required to pass before entering the fall state. Useful for walking down stairs")]
    public float FallTimeout = 0.15f;

    [Header("Player Grounded")]
    [Tooltip("If the character is grounded or not. Not part of the CharacterController built in grounded check")]
    public bool Grounded = true;

    [Tooltip("Useful for rough ground")]
    public float GroundedOffset = -0.14f;

    [Tooltip("The radius of the grounded check. Should match the radius of the CharacterController")]
    public float GroundedRadius = 0.28f;

    [Tooltip("What layers the character uses as ground")]
    public LayerMask GroundLayers;

    [Header("Cinemachine")]
    [Tooltip("The follow target set in the Cinemachine Virtual Camera that the camera will follow")]
    public GameObject CinemachineCameraTarget;

    [Tooltip("How far in degrees can you move the camera up")]
    public float TopClamp = 70.0f;

    [Tooltip("How far in degrees can you move the camera down")]
    public float BottomClamp = -30.0f;

    [Tooltip("For locking the camera position on all axis")]
    public bool LockCameraPosition = false;

    private Animator _anim;
    private CharacterController _controller;
    private GameObject _mainCamera;

    private float _speed = 0.0f;
    private float _targetRotation = 0.0f;
    private float _rotationVelocity;
    private float _verticalVelocity;

    // timeout deltatime
    private float _jumpTimeoutDelta = 0.0f;
    // limit jump height to JumpHeight
    private float _jumpTotalHeight = 0.0f;
    // current frame delta height
    public float _deltaHeight = 0.0f;

    public bool IsJumping = false;
    public bool IsFalling = false;

    // cinemachine
    private float _cinemachineTargetYaw;
    private float _cinemachineTargetPitch;

    public float GetSpeed() { return _speed; }

    void Awake()
    {
        _anim = GetComponent<Animator>();
        _controller = GetComponent<CharacterController>();
        _mainCamera = GameObject.FindGameObjectWithTag("MainCamera");

        Application.targetFrameRate = 30;
    }

    // Start is called before the first frame update
    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetKey(KeyCode.Escape))
            Cursor.lockState = CursorLockMode.None;
        else if (Input.GetMouseButtonDown(0))
            Cursor.lockState = CursorLockMode.Locked;

        // Store the input axes.
        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");

        GroundedCheck();

        JumpAndGravity();

        Move(h, v);
    }

    // ������Ӧ��root motion����Ĭ�ϵ�animator move����������Ĵ�������Ȼ������������player controller�ӹܵģ���Ҫ���ԡ�
    private void OnAnimatorMove()
    {
        transform.position += _anim.deltaPosition;
        transform.Rotate(_anim.deltaRotation.eulerAngles);
    }

    private void LateUpdate()
    {
        CameraRotation();
    }

    private void GroundedCheck()
    {
        // set sphere position, with offset
        Vector3 spherePosition = new Vector3(transform.position.x, transform.position.y - GroundedOffset,
            transform.position.z);
        Grounded = Physics.CheckSphere(spherePosition, GroundedRadius, GroundLayers,
            QueryTriggerInteraction.Ignore);

        _anim.SetBool("Grounded", Grounded);
    }

    void OnDrawGizmosSelected()
    {
        // Draw a yellow sphere at the transform's position
        //Vector3 spherePosition = new Vector3(transform.position.x, transform.position.y - GroundedOffset, transform.position.z);
        //Gizmos.color = Color.yellow;
        //Gizmos.DrawSphere(spherePosition, GroundedRadius);
    }

    private void JumpAndGravity()
    {
        _deltaHeight = _verticalVelocity * Time.deltaTime;
        _verticalVelocity += Gravity * Time.deltaTime;

        if (Grounded)
        {
            // stop our velocity dropping infinitely when grounded
            if (_verticalVelocity < 0.0f)
            {
                _verticalVelocity = -2f;
            }

            // stop falling
            IsFalling = false;

            bool isJump = Input.GetButtonDown("Jump");
            if (isJump && _jumpTimeoutDelta <= 0.0f)
            {
                // the square root of H * -2 * G = how much velocity needed to reach desired height
                _verticalVelocity = Mathf.Sqrt(JumpHeight * -2f * Gravity);
                IsJumping = true;
                _jumpTimeoutDelta = JumpTimeout;
            }
        }

        if (_jumpTimeoutDelta > 0.0f)
        {
            _jumpTimeoutDelta -= Time.deltaTime;
        }

        if (IsJumping)
        {
            _jumpTotalHeight += _deltaHeight;
            if (_jumpTotalHeight > JumpHeight)
            {
                // stop jumping and start falling
                IsJumping = false;
                IsFalling = true;
                _verticalVelocity = 0f;
                _jumpTotalHeight = 0f;
            }
        }

        _anim.SetBool("Jumping", IsJumping);
        _anim.SetBool("Falling", IsFalling);
    }

    void Move(float h, float v)
    {
        float targetSpeed = 0.0f;
        if (Input.GetKey(KeyCode.LeftShift))
        {
            targetSpeed = SprintSpeed;
        }
        else
        {
            targetSpeed = MoveSpeed;
        }

        Vector2 move = new Vector2(h, v);
        if (move == Vector2.zero)
        {
            targetSpeed = 0.0f;
        }

        // a reference to the players current horizontal velocity
        float currentHorizontalSpeed = new Vector3(_controller.velocity.x, 0.0f, _controller.velocity.z).magnitude;
        float speedOffset = 0.1f;

        if (currentHorizontalSpeed < targetSpeed - speedOffset ||
            currentHorizontalSpeed > targetSpeed + speedOffset)
        {
            _speed = Mathf.Lerp(_speed, targetSpeed, Time.deltaTime * SpeedChangeRate);
        }
        else
        {
            _speed = targetSpeed;
        }

        if (move != Vector2.zero)
        {
            Vector3 inputDirection = new Vector3(h, 0, v).normalized;
            // ����ĳ�����ǽ�ɫ�����泯�򣬺��߿��������ǰ�ߣ�����վ�Ų�����ֱ�ӵ�������ĳ�������Ҫ��ת�����������ת������ķ���
            _targetRotation = Mathf.Atan2(inputDirection.x, inputDirection.z) * Mathf.Rad2Deg + _mainCamera.transform.eulerAngles.y;

            // SmoothDampAngle���ǲ�ֵ��ʵ�֣�������һ��Ч����implements a critically damped harmonic oscillator�������Բ�Ҫ�ò�ֵ��˼άȥ������������
            // ͬʱ�����������Ҫһ��ref velocity�����������������������Ҫ֧�ŵ�����target��
            float rotation = Mathf.SmoothDampAngle(transform.eulerAngles.y, _targetRotation, ref _rotationVelocity, RotationSmoothTime);

            // rotate to face input direction relative to camera position
            transform.rotation = Quaternion.Euler(0.0f, rotation, 0.0f);
        }

        // �ƶ������ǹ̶��ģ�ת�������ƶ��Ĺ�������ɵ�
        Vector3 targetDirection = Quaternion.Euler(0.0f, _targetRotation, 0.0f) * Vector3.forward;

        // move the player
        _controller.Move(targetDirection.normalized * (_speed * Time.deltaTime) + new Vector3(0.0f, _deltaHeight, 0.0f));

        // ����
        _anim.SetFloat("Speed", _speed);
    }

    private void CameraRotation()
    {
        // h/v ���ﷵ�ص�������ƫ�����λ�������������꣬Ҳ���������أ����޹���Ҫ��rotSpeed�ĵ�λ�ǣ� �Ƕ�/ÿ��λƫ����������h/v����rotSpeed֮�����ֱ�ӵ����Ƕ����á�
        float h = Input.GetAxis("Mouse X");
        float v = Input.GetAxis("Mouse Y");

        // ����ֱ��rotate������һ���ȵ�ͷ90�ȣ�Ȼ������ת90�ȣ���ͷû���⣬����תʱ��������Y�ᣨSpace.Self����ת90�ȣ�����ͷ90��ʱY��ͬ���Ѿ���X����ת��90�ȣ�����Ч������ȫ�����ˡ�
        // ��ȷ�ķ�ʽ�����������ת���ֱ��ۼӣ�����������֮��Ͳ���������ţ������������������Ԫ������ʾ��ת�����������������⡣
        // CinemachineCameraTarget.transform.Rotate(v, h, 0);

        if ((h != 0f || v != 0f) && !LockCameraPosition)
        {
            _cinemachineTargetYaw += h * rotSpeed;
            _cinemachineTargetPitch += v * rotSpeed;
        }

        // clamp our rotations so our values are limited 360 degrees
        _cinemachineTargetYaw = ClampAngle(_cinemachineTargetYaw, float.MinValue, float.MaxValue);
        _cinemachineTargetPitch = ClampAngle(_cinemachineTargetPitch, BottomClamp, TopClamp);

        // pitch draw me crazy
        _cinemachineTargetPitch = 0f;

        // Cinemachine will follow this target
        CinemachineCameraTarget.transform.rotation = Quaternion.Euler(_cinemachineTargetPitch, _cinemachineTargetYaw, 0.0f);
    }

    private static float ClampAngle(float lfAngle, float lfMin, float lfMax)
    {
        if (lfAngle < -360f) lfAngle += 360f;
        if (lfAngle > 360f) lfAngle -= 360f;
        return Mathf.Clamp(lfAngle, lfMin, lfMax);
    }
}

