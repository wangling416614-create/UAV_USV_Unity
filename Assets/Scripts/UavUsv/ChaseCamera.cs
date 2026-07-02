using UnityEngine;

namespace UavUsv
{
    public sealed class ChaseCamera : MonoBehaviour
    {
        public Transform target;
        public Transform companion;
        public Transform lookAt;
        public float distance = 8.5f;
        public float height = 3.2f;
        public float sideOffset = -1.1f;
        public float minDistance = 10.5f;
        public float maxDistance = 28f;
        public float minHeight = 4.2f;
        public float maxHeight = 11f;
        public float lookAhead = 4.5f;
        public float lookHeight = 1.2f;
        public float lighthouseInfluence = .14f;
        public float positionSmooth = 4.5f;
        public float rotationSmooth = 6f;
        public bool useTargetRightAsForward;

        private bool initialized;

        private void LateUpdate()
        {
            if (!target)
                return;

            Vector3 forward = useTargetRightAsForward ? target.right : target.forward;
            forward.y = 0f;

            if (forward.sqrMagnitude < .001f)
            {
                Vector3 fallback = lookAt ? lookAt.position - target.position : Vector3.forward;
                fallback.y = 0f;
                forward = fallback.sqrMagnitude > .001f ? fallback.normalized : Vector3.forward;
            }
            else
            {
                forward.Normalize();
            }

            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            Vector3 subjectCenter = target.position;
            float subjectSpread = 0f;
            if (companion)
            {
                subjectCenter = (target.position + companion.position) * .5f;
                subjectSpread = Vector3.Distance(target.position, companion.position);
            }

            if (lookAt)
            {
                Vector3 lighthouseFlat = lookAt.position;
                lighthouseFlat.y = subjectCenter.y;
                subjectSpread = Mathf.Max(subjectSpread, Vector3.Distance(target.position, lighthouseFlat) * .18f);
            }

            float dynamicDistance = Mathf.Clamp(distance + subjectSpread * .55f, minDistance, maxDistance);
            float dynamicHeight = Mathf.Clamp(height + subjectSpread * .18f, minHeight, maxHeight);
            Vector3 desiredPosition = subjectCenter - forward * dynamicDistance + right * sideOffset + Vector3.up * dynamicHeight;

            Vector3 focusPoint = subjectCenter + Vector3.up * lookHeight;
            if (lookAt)
                focusPoint = Vector3.Lerp(focusPoint, lookAt.position + Vector3.up * 2.2f, lighthouseInfluence);

            if (!initialized)
            {
                transform.position = desiredPosition;
                initialized = true;
            }
            else
            {
                float positionT = 1f - Mathf.Exp(-positionSmooth * Time.deltaTime);
                transform.position = Vector3.Lerp(transform.position, desiredPosition, positionT);
            }

            Quaternion desiredRotation = Quaternion.LookRotation(focusPoint - transform.position, Vector3.up);
            float rotationT = 1f - Mathf.Exp(-rotationSmooth * Time.deltaTime);
            transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, rotationT);
        }
    }
}
