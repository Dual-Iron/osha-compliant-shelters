using Menu.Remix.MixedUI;
using UnityEngine;

namespace OshaShelters;

sealed class Options : OptionInterface
{
    public static Configurable<float> SleepTime;
    public static Configurable<bool> SleepTogether;
    public static Configurable<bool> SaveExcess;

    public Options()
    {
        SleepTime = config.Bind("cfgSleepTime", 3f, new ConfigAcceptableRange<float>(1, 10));
        SleepTogether = config.Bind("cfgSleepTogether", true);
        SaveExcess = config.Bind("cfgSaveExcess", true);
    }

    public override void Initialize()
    {
        base.Initialize();

        Tabs = new OpTab[] { new OpTab(this) };

        var author = new OpLabel(20, 600 - 40, "by Dual", true);
        var github = new OpLabel(20, 600 - 40 - 40, "github.com/Dual-Iron/osha-compliant-shelters");

        var desc = new OpLabel(new(100, 340), Vector2.zero, "Close shelter door after holding DOWN for this many seconds:", FLabelAlignment.Left);
        var slider = new OpFloatSlider(SleepTime, new Vector2(104, 292), 300, decimalNum: 1, vertical: false);

        var desc2 = new OpLabel(new(320, 230), Vector2.zero, "All living players must hold DOWN in co-op:", FLabelAlignment.Right);
        var checkbox = new OpCheckBox(SleepTogether, new(352, 226));

        var desc3 = new OpLabel(new(320, 180), Vector2.zero, "Disable destroying extra items in shelter:", FLabelAlignment.Right);
        var checkbox2 = new OpCheckBox(SaveExcess, new(352, 176));

        Tabs[0].AddItems(
            author,
            github,
            desc,
            slider,
            desc2,
            checkbox,
            desc3,
            checkbox2
        );
    }
}
