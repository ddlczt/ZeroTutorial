using UnityEngine;

public class PlayerController : MonoBehaviour
{
    CombatComponent combatComponent;
    Animator _anim;

    void Awake()
    {
    }

    // Start is called before the first frame update
    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        combatComponent = GetComponent<CombatComponent>();
        _anim = GetComponent<Animator>();
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetKey(KeyCode.Escape))
            Cursor.lockState = CursorLockMode.None;
        else if (Input.GetKey(KeyCode.RightAlt))
            Cursor.lockState = CursorLockMode.Locked;

        CheckCombatInput();
    }

    void CheckCombatInput()
    {
        if (Input.GetButtonDown("Fire"))
        {
            combatComponent.NormalAttack();
        }
        else if (Input.GetKey(KeyCode.Q))
        {
            combatComponent.SkillAttack(0);
        }
    }

    // ������Ӧ��root motion����Ĭ�ϵ�animator move����������Ĵ�������Ȼ������������CharacterMovement�ӹܵģ���Ҫ���ԡ�
    private void OnAnimatorMove()
    {
        if (!_anim) return;
        transform.position += _anim.deltaPosition;
        transform.Rotate(_anim.deltaRotation.eulerAngles);
    }
}

