using UnityEngine;
using Oheangbu.Data.Demo;

namespace Oheangbu.Data.World
{
    [CreateAssetMenu(menuName = "Oheangbu/UI/Equipment Art")]
    public sealed class EquipmentUiArtSO : ScriptableObject
    {
        public Sprite Portrait;
        // Index matches EquipmentSlot. Art carries no ownership or gameplay state.
        public Sprite[] Slots = new Sprite[6];
        public Sprite ForSlot(EquipmentSlot slot)
        {
            int index = (int)slot;
            return Slots != null && index >= 0 && index < Slots.Length ? Slots[index] : null;
        }
    }
}
