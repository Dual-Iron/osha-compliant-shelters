using Menu.Remix.MixedUI;
using UnityEngine;

namespace OshaShelters;

sealed class Options : OptionInterface
{
    public static Configurable<float> SleepTime;
    public static Configurable<bool> HoldDown;
    public static Configurable<bool> SleepTogether;
    public static Configurable<bool> SaveExcess;

    public Options()
    {
        SleepTime = config.Bind("cfgSleepTime", 3f, new ConfigAcceptableRange<float>(1, 10));
        HoldDown = config.Bind("cfgHoldDown", true);
        SleepTogether = config.Bind("cfgSleepTogether", true);
        SaveExcess = config.Bind("cfgSaveExcess", true);
    }

    OpCheckBox holdDown;
    OpCheckBox coopHoldDown;

    public override void Initialize()
    {
        base.Initialize();

        Tabs = new OpTab[] { new OpTab(this) };

        var author = new OpLabel(20, 600 - 40, "by Dual", true);
        var github = new OpLabel(20, 600 - 40 - 40, "github.com/Dual-Iron/osha-compliant-shelters");

        float y = 340;

        var a = new OpLabel(new(20, y), Vector2.zero, "Close shelter door after holding DOWN for this many seconds:", FLabelAlignment.Left);
        var a2 = new OpFloatSlider(SleepTime, new Vector2(24, y - 48), 300, decimalNum: 1, vertical: false);

        var b = new OpLabel(new(52, y -= 110), Vector2.zero, "Hold DOWN/JUMP to sleep", FLabelAlignment.Left);
        holdDown = new OpCheckBox(HoldDown, new(20, y - 2));

        var c = new OpLabel(new(52, y -= 34), Vector2.zero, "All living players must hold DOWN in co-op", FLabelAlignment.Left);
        coopHoldDown = new OpCheckBox(SleepTogether, new(20, y - 2));

        var d = new OpLabel(new(52, y -= 34), Vector2.zero, "Disable destroying extra items in shelter", FLabelAlignment.Left);
        var d2 = new OpCheckBox(SaveExcess, new(20, y - 2));

        Tabs[0].AddItems(author, github, a, a2, b, coopHoldDown, c, holdDown, d, d2);
    }

    public override void Update()
    {
        base.Update();

        if (holdDown != null) {
            bool greyed = holdDown.value != "true";

            coopHoldDown.greyedOut = greyed;
        }
    }
}
