using UnityEngine;

namespace Afterecho
{
    [CreateAssetMenu(menuName="Afterecho/Runner Rules")]
    public sealed class RunnerRules : ScriptableObject
    {
        [Min(1)] public int maxHealth=100;
        [Min(0)] public int hitRecovery=1, missDamage=10, extraDamage=3;
        [Min(1)] public int boostCombo=15;
        [Min(1)] public float lapSeconds=12;
        [Min(1)] public float boostSpeed=2.4f;
        [Range(.6f,2)] public float previewSeconds=1.2f;
        [Range(.1f,.6f)] public float fallSeconds=.35f;
        public Color orange=new Color(1,.64f,.13f);
        public Color cyan=new Color(.16f,.78f,.84f);
        public Color magenta=new Color(.96f,.29f,.60f);
    }
}
