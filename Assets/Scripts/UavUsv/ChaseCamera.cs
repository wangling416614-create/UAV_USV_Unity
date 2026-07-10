using UnityEngine;

namespace UavUsv
{
    public sealed class ChaseCamera : MonoBehaviour
    {
        private enum ViewMode
        {
            BoatFollow,
            DroneFollow,
            Overview,
            FreeOrbit
        }

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

        private ViewMode mode = ViewMode.BoatFollow;
        private bool initialized;
        private float orbitYaw = -35f;
        private float orbitPitch = 22f;
        private float orbitDistance = 42f;

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Alpha1))
                SetMode(ViewMode.BoatFollow);
            else if (Input.GetKeyDown(KeyCode.Alpha2))
                SetMode(ViewMode.DroneFollow);
            else if (Input.GetKeyDown(KeyCode.Alpha3))
                SetMode(ViewMode.Overview);
            else if (Input.GetKeyDown(KeyCode.Alpha4))
                SetMode(ViewMode.FreeOrbit);
            else if (Input.GetKeyDown(KeyCode.Tab))
                SetMode((ViewMode)(((int)mode + 1) % 4));

            if (mode == ViewMode.FreeOrbit)
            {
                if (Input.GetMouseButton(1))
                {
                    orbitYaw += Input.GetAxis("Mouse X") * 4f;
                    orbitPitch = Mathf.Clamp(
                        orbitPitch - Input.GetAxis("Mouse Y") * 3f,
                        8f,
                        78f
                    );
                }

                orbitDistance = Mathf.Clamp(
                    orbitDistance - Input.mouseScrollDelta.y * 3f,
                    8f,
                    180f
                );
            }
        }

        private void LateUpdate()
        {
            if (!target)
                return;

            if (mode == ViewMode.FreeOrbit)
            {
                UpdateFreeOrbit();
                return;
            }

            if (mode == ViewMode.Overview)
            {
                UpdateOverview();
                return;
            }

            if (mode == ViewMode.DroneFollow && companion)
            {
                UpdateSubjectFollow(
                    companion,
                    target,
                    18f,
                    8.5f,
                    1.1f,
                    1.6f,
                    false
                );
                return;
            }

            UpdateSubjectFollow(
                target,
                companion,
                distance,
                height,
                sideOffset,
                lookHeight,
                useTargetRightAsForward
            );
        }

        private void UpdateSubjectFollow(
            Transform subject,
            Transform secondary,
            float baseDistance,
            float baseHeight,
            float side,
            float focusHeight,
            bool targetRightAsForward)
        {
            Vector3 forward = targetRightAsForward ? subject.right : subject.forward;
            forward.y = 0f;

            if (forward.sqrMagnitude < .001f)
            {
                Vector3 fallback = lookAt
                    ? lookAt.position - subject.position
                    : Vector3.forward;
                fallback.y = 0f;
                forward = fallback.sqrMagnitude > .001f ? fallback.normalized : Vector3.forward;
            }
            else
            {
                forward.Normalize();
            }

            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            Vector3 subjectCenter = subject.position;
            float subjectSpread = 0f;
            if (secondary)
            {
                subjectCenter = (subject.position + secondary.position) * .5f;
                subjectSpread = Vector3.Distance(subject.position, secondary.position);
            }

            if (lookAt)
            {
                Vector3 lighthouseFlat = lookAt.position;
                lighthouseFlat.y = subjectCenter.y;
                subjectSpread = Mathf.Max(
                    subjectSpread,
                    Vector3.Distance(subject.position, lighthouseFlat) * .18f
                );
            }

            float dynamicDistance = Mathf.Clamp(
                baseDistance + subjectSpread * .55f,
                minDistance,
                maxDistance
            );
            float dynamicHeight = Mathf.Clamp(
                baseHeight + subjectSpread * .18f,
                minHeight,
                maxHeight
            );
            Vector3 desiredPosition =
                subjectCenter - forward * dynamicDistance +
                right * side +
                Vector3.up * dynamicHeight;

            Vector3 focusPoint = subjectCenter + Vector3.up * focusHeight;
            if (lookAt)
                focusPoint = Vector3.Lerp(focusPoint, lookAt.position + Vector3.up * 2.2f, lighthouseInfluence);

            MoveCamera(desiredPosition, focusPoint);
        }

        private void UpdateOverview()
        {
            Vector3 focus = GetSceneFocus();
            Vector3 desiredPosition = focus + new Vector3(0f, 92f, -28f);
            MoveCamera(desiredPosition, focus);
        }

        private void UpdateFreeOrbit()
        {
            Vector3 focus = GetSceneFocus();
            Quaternion rotation = Quaternion.Euler(orbitPitch, orbitYaw, 0f);
            Vector3 desiredPosition =
                focus - rotation * Vector3.forward * orbitDistance;
            MoveCamera(desiredPosition, focus);
        }

        private Vector3 GetSceneFocus()
        {
            Vector3 focus = target.position;
            if (companion)
                focus = (focus + companion.position) * .5f;
            if (lookAt)
                focus = Vector3.Lerp(focus, lookAt.position, .18f);
            focus.y += 1.2f;
            return focus;
        }

        private void MoveCamera(Vector3 desiredPosition, Vector3 focusPoint)
        {
            if (!initialized)
            {
                transform.position = desiredPosition;
                initialized = true;
            }
            else
            {
                float positionT = 1f - Mathf.Exp(-positionSmooth * Time.deltaTime);
                transform.position = Vector3.Lerp(
                    transform.position,
                    desiredPosition,
                    positionT
                );
            }

            Quaternion desiredRotation = Quaternion.LookRotation(
                focusPoint - transform.position,
                Vector3.up
            );
            float rotationT = 1f - Mathf.Exp(-rotationSmooth * Time.deltaTime);
            transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, rotationT);
        }

        private void SetMode(ViewMode nextMode)
        {
            if (mode == nextMode)
                return;

            mode = nextMode;
            initialized = false;
            if (mode == ViewMode.FreeOrbit)
            {
                Vector3 euler = transform.rotation.eulerAngles;
                orbitYaw = euler.y;
                orbitPitch = Mathf.Clamp(euler.x, 8f, 78f);
                orbitDistance = Mathf.Clamp(
                    Vector3.Distance(transform.position, GetSceneFocus()),
                    8f,
                    180f
                );
            }
        }
    }
}
