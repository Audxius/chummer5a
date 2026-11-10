using System;
using Chummer.Backend.Equipment;

namespace Chummer.Rules
{
    /// <summary>Legacy inventory condition-monitor bindings for vehicles and matrix devices.</summary>
    public static class EquipmentConditionRules
    {
        public static RuleResult SetDamage(object item, bool physical, int boxes)
        {
            if (physical)
            {
                Vehicle vehicle = item as Vehicle;
                if (vehicle == null) return RuleResult.NotAllowed;
                int value = Math.Max(0, Math.Min(boxes, vehicle.PhysicalCM));
                if (vehicle.PhysicalCMFilled == value) return RuleResult.NotAllowed;
                vehicle.PhysicalCMFilled = value;
            }
            else
            {
                IHasMatrixAttributes device = item as IHasMatrixAttributes;
                if (device == null) return RuleResult.NotAllowed;
                int value = Math.Max(0, Math.Min(boxes, device.MatrixCM));
                if (device.MatrixCMFilled == value) return RuleResult.NotAllowed;
                device.MatrixCMFilled = value;
            }
            return RuleResult.Changed;
        }
    }
}
