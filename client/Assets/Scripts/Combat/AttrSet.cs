using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AttrSet : MonoBehaviour
{
    public int MaxHealth;
    public int Health;
    public int MaxMana;
    public int Mana;
    public int Attack;
    public float AttackSpeed = 1;
    public float AdditionalAttackSpeed = 0;

    public int Defence;
    public float CriticalRate;
    public float CriticalDamage;
    // ������
    [Range(0.0f, 1.0f)]
    public float Accuracy;
    // ����
    [Range(0.0f, 1.0f)]
    public float DodgeRate;
    // ����
    public int Shield;
    // ����
    [Range(0.0f, 1.0f)]
    public float Tenacity;
    // ��Ѫ
    [Range(0.0f, 1.0f)]
    public float Lifesteal;

    // ״̬����ѣ�����ᡢ���塣������
    private int Status;

    // Start is called before the first frame update
    void Start()
    {
        Health = MaxHealth;
        Mana = MaxMana;
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void TakeDamage(AttrSet attacker, int attackDamage)
    {
        // 1. �����˺�����
        int baseDamage = Mathf.Max(1, attackDamage - this.Defence);

        // 2. �����ж�
        bool isCritical = Random.value < attacker.CriticalRate;
        float damage = isCritical ? baseDamage * (1 + attacker.CriticalDamage) : baseDamage;

        // 3. �����������ж�
        bool isHit = Random.value < (attacker.Accuracy - this.DodgeRate);
        if (!isHit)
        {
            Debug.Log("����δ���У�");
            damage = 0;
        }

        // 4. ��������
        int shieldAbsorb = Mathf.Min((int)damage, this.Shield);
        this.Shield -= shieldAbsorb;
        damage -= shieldAbsorb;

        // 5. ʵ�ʿ�Ѫ
        this.Health -= (int)damage;
        this.Health = Mathf.Max(0, this.Health); // ȷ������ֵ������0

        // 6. ��ѪЧ��
        if (damage > 0 && attacker.Lifesteal > 0)
        {
            int healAmount = (int)(damage * attacker.Lifesteal);
            attacker.Health += healAmount;
            attacker.Health = Mathf.Min(attacker.MaxHealth, attacker.Health); // ȷ������ֵ����������
        }

        // �����־
        Debug.Log($"����������˺�: {damage}, �Ƿ񱩻�: {isCritical}, ��������: {shieldAbsorb}, ������ʣ������: {this.Health}");
    }

}
