using UnityEngine;

namespace MiningSimulator.Ores
{
    internal static class RagdollPhysics
    {
        internal static void Release(Rigidbody[] bodies, Vector3 velocity, Vector3 angularVelocity)
        {
            velocity = Vector3.ClampMagnitude(velocity, 25f);
            foreach (var body in bodies)
                if (body) PrepareBody(body);

            Physics.SyncTransforms();
            foreach (var body in bodies)
            {
                if (!body) continue;
                body.isKinematic = false;
                body.useGravity = true;
                body.linearVelocity = velocity;
                body.angularVelocity = angularVelocity;
                body.WakeUp();
            }
        }

        internal static void TipStandingCorpse(Rigidbody[] bodies, Vector3 fallDirection, float turnSpeed = 3.2f)
        {
            // Rotate around the stance, sharing the motion across joints instead of yanking one bone.
            Vector3 pivot = bodies[0].worldCenterOfMass - Vector3.up * .4f;
            Vector3 angularVelocity = Vector3.Cross(Vector3.up, fallDirection.normalized) * turnSpeed;
            foreach (var body in bodies)
            {
                if (!body) continue;
                body.linearVelocity += Vector3.Cross(angularVelocity, body.worldCenterOfMass - pivot);
                body.angularVelocity = angularVelocity;
            }
        }

        private static void PrepareBody(Rigidbody body)
        {
            body.interpolation = RigidbodyInterpolation.None;
            body.solverIterations = 12;
            body.solverVelocityIterations = 6;
            body.maxDepenetrationVelocity = 2f;
            body.maxAngularVelocity = 5f;
            body.angularDamping = 2.5f;

            var joint = body.GetComponent<CharacterJoint>();
            if (!joint || !joint.connectedBody) return;
            joint.autoConfigureConnectedAnchor = false;
            joint.connectedAnchor = joint.connectedBody.transform.InverseTransformPoint(body.transform.TransformPoint(joint.anchor));
            joint.enableCollision = false;
            joint.enablePreprocessing = false;
            joint.enableProjection = true;
            joint.projectionDistance = .018f;
            joint.projectionAngle = 8f;

            string name = body.name.ToLowerInvariant();
            bool hinge = name.Contains("forearm") || name.Contains("shin") ||
                (name.EndsWith("leg") && !name.EndsWith("upleg"));
            bool smallJoint = name.Contains("hand") || name.Contains("foot") || name.Contains("head");
            float twist = hinge ? 60f : smallJoint ? 20f : 28f;
            float swing = hinge ? 8f : smallJoint ? 25f : 42f;
            joint.lowTwistLimit = new SoftJointLimit { limit = -twist };
            joint.highTwistLimit = new SoftJointLimit { limit = twist };
            joint.swing1Limit = new SoftJointLimit { limit = swing };
            joint.swing2Limit = new SoftJointLimit { limit = hinge ? 8f : swing * .8f };
        }
    }
}
