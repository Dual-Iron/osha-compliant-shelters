using Menu.Remix.MixedUI;
using UnityEngine;

namespace OshaShelters;

sealed class Options : OptionInterface
{
    public static Configurable<float> SleepTime;
    public static Configurable<bool> HoldJump;
    public static Configurable<bool> SleepTogether;
    public static Configurable<bool> SaveExcess;

    public Options()
    {
        SleepTime = config.Bind("cfgSleepTime", 3f, new ConfigAcceptableRange<float>(1, 10));
        HoldJump = config.Bind("cfgHoldJump", true);
        SleepTogether = config.Bind("cfgSleepTogether", true);
        SaveExcess = config.Bind("cfgSaveExcess", true);
    }

    public override void Initialize()
    {
        base.Initialize();

        Tabs = new OpTab[] { new OpTab(this) };

        float y = 380;

        var author = new OpLabel(20, 600 - 40, "by Dual", true);
        var github = new OpLabel(20, 600 - 40 - 40, "github.com/Dual-Iron/osha-compliant-shelters");

        var ds = new OpLabel(new(100, y), Vector2.zero, "Close shelter door after holding DOWN for this many seconds:", FLabelAlignment.Left);
        var s = new OpFloatSlider(SleepTime, new Vector2(104, y - 48), 300, decimalNum: 1, vertical: false);

        var d1 = new OpLabel(new(320, y -= 110), Vector2.zero, "Allow holding JUMP instead of DOWN (recommended):", FLabelAlignment.Right);
        var c1 = new OpCheckBox(HoldJump, new(352, y - 4));

        var d2 = new OpLabel(new(320, y -= 50), Vector2.zero, "All living players must hold DOWN in co-op:", FLabelAlignment.Right);
        var c2 = new OpCheckBox(SleepTogether, new(352, y - 4));

        var d3 = new OpLabel(new(320, y -= 50), Vector2.zero, "Disable destroying extra items in shelter:", FLabelAlignment.Right);
        var c3 = new OpCheckBox(SaveExcess, new(352, y - 4));

        Tabs[0].AddItems(
            author,
            github,
            ds,
            s,
            d1,
            c1,
            d2,
            c2,
            d3,
            c3
        );
    }
}
