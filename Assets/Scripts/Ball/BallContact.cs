using UnityEngine;

namespace FiniGolf
{
    /// <summary>Forwards ball collisions to the BallManager (for sounds).</summary>
    public class BallContact : MonoBehaviour
    {
        private void OnCollisionEnter(Collision collision)
        {
            if (BallManager.Instance != null) BallManager.Instance.OnBallCollision(collision, true);
        }

        private void OnCollisionStay(Collision collision)
        {
            if (BallManager.Instance != null) BallManager.Instance.OnBallCollision(collision, false);
        }
    }
}
