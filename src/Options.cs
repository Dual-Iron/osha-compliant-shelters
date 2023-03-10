using Menu.Remix.MixedUI;
using UnityEngine;

namespace OshaShelters;

sealed class Options : OptionInterface
{
    public static Configurable<float> SleepTime;
    public static Configurable<bool> SleepTogether;

    public Options()
    {
        SleepTime = config.Bind("cfgSleepTime", 3f, new ConfigAcceptableRange<float>(1, 10));
        SleepTogether = config.Bind("cfgSleepTogether", true);
    }

    public override void Initialize()
    {
        base.Initialize();

        Tabs = new OpTab[] { new OpTab(this) };

        var author = new OpLabel(20, 600 - 40, "by Dual", true);
        var github = new OpLabel(20, 600 - 40 - 40, "github.com/Dual-Iron/osha-compliant-shelters");

        var desc = new OpLabel(new(100, 330), Vector2.zero, "Close shelter door after holding DOWN for this many seconds:", FLabelAlignment.Left);
        var slider = new OpFloatSlider(SleepTime, new Vector2(104, 282), 300, decimalNum: 1, vertical: false);

        var desc2 = new OpLabel(new(320, 220), Vector2.zero, "All living players must hold DOWN in co-op:", FLabelAlignment.Right);
        var checkbox = new OpCheckBox(SleepTogether, new(352, 216));

        Tabs[0].AddItems(
            author,
            github,
            desc,
            slider,
            desc2,
            checkbox
        );
    }
}
