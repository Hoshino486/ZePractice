using ZePractice;

int passed = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAIL " + name);
    passed++; Console.WriteLine("PASS " + name);
}
void Reject(Action action, string name)
{
    bool rejected = false;
    try { action(); } catch (InvalidDataException) { rejected = true; }
    Check(rejected, name);
}

ZePracticeConfig Config(int zombieHealth, int zombieArmor, int humanHealth, int humanArmor, int armorRegeneration = 125, int zombieHealthLimit = 1000000)
{
    var config = new ZePracticeConfig();
    config.PublicGameplay.ZombieHealth = zombieHealth;
    config.PublicGameplay.ZombieHealthLimit = zombieHealthLimit;
    config.PublicGameplay.ZombieArmor = zombieArmor;
    config.PublicGameplay.HumanHealthLimit = humanHealth;
    config.PublicGameplay.HumanArmorLimit = humanArmor;
    config.PublicGameplay.ArmorRegenerationPerSecond = armorRegeneration;
    return config;
}

Config(1000000, 1000000, 1000000, 1000000, 1000000).PublicGameplay.Validate();
Check(true, "accepts 1,000,000 zombie/human health, armor and armor regeneration limits");
Config(35000, 2500, 1000000, 1000000).PublicGameplay.Validate();
Check(true, "accepts zombie spawn health 35,000 with a 1,000,000 settable ceiling");
Check(Config(35000, 2500, 35000, 2500).PublicGameplay.ZombieHealthLimit == 1000000, "default settable zombie health ceiling is 1,000,000 while spawn stays 35,000");
Reject(() => Config(1000001, 2500, 35000, 2500).PublicGameplay.Validate(), "rejects zombie spawn health above 1,000,000");
Reject(() => Config(35000, 1000001, 35000, 2500).PublicGameplay.Validate(), "rejects zombie armor above 1,000,000");
Reject(() => Config(35000, 2500, 1000001, 2500).PublicGameplay.Validate(), "rejects human health limit above 1,000,000");
Reject(() => Config(35000, 2500, 35000, 1000001).PublicGameplay.Validate(), "rejects human armor limit above 1,000,000");
Reject(() => Config(35000, 2500, 35000, 2500, 1000001).PublicGameplay.Validate(), "rejects armor regeneration above 1,000,000");
Reject(() => Config(35000, 2500, 35000, 2500, 125, 1000001).PublicGameplay.Validate(), "rejects settable zombie health ceiling above 1,000,000");
Reject(() => Config(35000, 2500, 35000, 2500, 125, 0).PublicGameplay.Validate(), "rejects settable zombie health ceiling below 1");
Reject(() => Config(0, 2500, 35000, 2500).PublicGameplay.Validate(), "rejects zombie health below 1");
Reject(() => Config(35000, -1, 35000, 2500).PublicGameplay.Validate(), "rejects negative zombie armor");
Console.WriteLine($"CONFIG_LIMIT_TESTS {passed}/{passed} passed");
