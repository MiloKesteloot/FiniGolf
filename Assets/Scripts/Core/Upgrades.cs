// This file and every function within was written by Claude Opus 5.5
using UnityEngine;

namespace FiniGolf
{
    public enum UpgradeType
    {
        None,
        BeachBall,
        PowerShot,
        Magnet,
        Mulligan,
    }

    /// <summary>Physics + feel settings for the ball. Upgrades swap these out for one shot.</summary>
    [System.Serializable]
    public struct BallProfile
    {
        public float radiusMultiplier;
        public float mass;
        public float gravityScale;
        public float linearDamping;
        public float angularDamping;
        public float rollingDeceleration;
        public float bounciness;
        public float powerMultiplier;
        public float magnetStrength;
        public bool beachBallVisual;

        public static BallProfile Default => new BallProfile
        {
            radiusMultiplier = 1f,
            mass = 0.045f,
            gravityScale = 1f,
            linearDamping = 0.06f,
            angularDamping = 0.05f,
            rollingDeceleration = 0.85f,
            bounciness = 0.45f,
            powerMultiplier = 1f,
            magnetStrength = 0f,
            beachBallVisual = false,
        };
    }

    public static class Upgrades
    {
        public static readonly UpgradeType[] Pickable =
        {
            UpgradeType.BeachBall, UpgradeType.PowerShot, UpgradeType.Magnet, UpgradeType.Mulligan,
        };

        public static string DisplayName(UpgradeType type)
        {
            switch (type)
            {
                case UpgradeType.BeachBall: return "Beach Ball";
                case UpgradeType.PowerShot: return "Power Shot";
                case UpgradeType.Magnet: return "Hole Magnet";
                case UpgradeType.Mulligan: return "Mulligan";
                default: return "";
            }
        }

        public static string Description(UpgradeType type)
        {
            switch (type)
            {
                case UpgradeType.BeachBall: return "Big, light and floaty. Falls slowly and bounces high.";
                case UpgradeType.PowerShot: return "Your next shot hits 70% harder.";
                case UpgradeType.Magnet: return "The cup pulls your ball in.";
                case UpgradeType.Mulligan: return "Your next shot is free and doesn't count.";
                default: return "";
            }
        }

        public static Color Tint(UpgradeType type)
        {
            switch (type)
            {
                case UpgradeType.BeachBall: return new Color(1f, 0.55f, 0.2f);
                case UpgradeType.PowerShot: return new Color(1f, 0.25f, 0.3f);
                case UpgradeType.Magnet: return new Color(0.65f, 0.4f, 1f);
                case UpgradeType.Mulligan: return new Color(0.2f, 0.9f, 0.55f);
                default: return Color.white;
            }
        }

        public static BallProfile Profile(UpgradeType type)
        {
            var p = BallProfile.Default;
            switch (type)
            {
                case UpgradeType.BeachBall:
                    p.radiusMultiplier = 1.8f;
                    p.mass = 0.012f;
                    p.gravityScale = 0.38f;
                    p.linearDamping = 0.55f;
                    p.angularDamping = 0.15f;
                    p.rollingDeceleration = 0.9f;
                    p.bounciness = 0.85f;
                    p.powerMultiplier = 1.05f;
                    p.beachBallVisual = true;
                    break;
                case UpgradeType.PowerShot:
                    p.powerMultiplier = 1.7f;
                    break;
                case UpgradeType.Magnet:
                    p.magnetStrength = 7f;
                    break;
            }
            return p;
        }
    }
}
