namespace UltiSim.Core;

// Fixed party slot ids. 0,1 = tanks · 2,3 = healers · 4-7 = DPS.
// Slot order matches PartyPresets.Standard so role index == preset array index.
public enum PartyRole
{
    MainTank = 0,       // Warrior slot
    OffTank = 1,        // Paladin slot
    RegenHealer = 2,    // White Mage slot
    ShieldHealer = 3,   // Scholar slot
    MeleeDpsA = 4,      // Dragoon slot
    MeleeDpsB = 5,      // Monk slot
    PhysRangedDps = 6,  // Bard slot
    CasterDps = 7,      // Black Mage slot
}
