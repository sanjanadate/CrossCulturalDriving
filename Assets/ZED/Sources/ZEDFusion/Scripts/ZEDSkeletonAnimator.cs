using Unity.Netcode;
using UnityEngine;

public class ZEDSkeletonAnimator : MonoBehaviour {
    public Animator animator;
    private HeightOffsetter heightOffsetter;

    /// --------------------------------------------
    /// -----------------Foot Locking---------------
    /// --------------------------------------------

    #region Foot Locking

    /// <summary>
    ///     Check if the SDK ankle keypoints have moved far enough from their previous position (on the horizontal plane).
    ///     If not, it'll be considered jittering, and should be taken into account in the foot IK.
    ///     This method should be called when the ZEDBodyTrackingManager updates the handler, as it only relies on the SDK
    ///     positions.
    /// </summary>
    /// <param name="newPosAnkleL"></param>
    /// <param name="newPosAnkleR"></param>
    public void CheckFootLock(Vector3 newPosAnkleL, Vector3 newPosAnkleR) {
        lockFootL = HorizontalDist(curPosAnkleL, newPosAnkleL) < thresholdFootLock;
        lockFootR = HorizontalDist(curPosAnkleR, newPosAnkleR) < thresholdFootLock;
        if (!lockFootL) curPosAnkleL = newPosAnkleL;
        if (!lockFootR) curPosAnkleR = newPosAnkleR;
    }

    #endregion

    /// -----------------------------------------
    /// ------------ UPPER BODY MODE ------------
    /// -----------------------------------------
    public void UpdateNavigationAndLegAnimationData() {
        // Stabilize Y position
        var raycastHit = new RaycastHit();
        if (Physics.Raycast(Skhandler.TargetBodyPositionWithHipOffset + new Vector3(0, 1, 0),
                Vector3.down, out raycastHit, 3f))
            //transform.position = new Vector3(skhandler.TargetBodyPositionWithHipOffset.x, raycastHit.point.y, skhandler.TargetBodyPositionWithHipOffset.z);
            Skhandler.TargetBodyPositionWithHipOffset = new Vector3(Skhandler.TargetBodyPositionWithHipOffset.x,
                raycastHit.point.y, Skhandler.TargetBodyPositionWithHipOffset.z);
        else
            //transform.position = new Vector3 (skhandler.TargetBodyPositionWithHipOffset.x, 0, skhandler.TargetBodyPositionWithHipOffset.z);
            Skhandler.TargetBodyPositionWithHipOffset = new Vector3(Skhandler.TargetBodyPositionWithHipOffset.x, 0,
                Skhandler.TargetBodyPositionWithHipOffset.z);


        // Take only rotation on Y axis.
        //transform.rotation = Quaternion.Euler(0,skhandler.TargetBodyOrientationSmoothed.eulerAngles.y,0);
        Skhandler.TargetBodyOrientationSmoothed =
            Quaternion.Euler(0, Skhandler.TargetBodyOrientationSmoothed.eulerAngles.y, 0);
        transform.rotation = Skhandler.TargetBodyOrientationSmoothed;

        // Find anim speed depending on if going forward or backward
        //float sc = Vector3.Dot(transform.forward, skhandler.rootVelocity);
        var sc = Vector3.Dot(transform.forward, Skhandler.rootVelocity);
        var animSpeed = sc >= 0
            ? Skhandler.rootVelocity.magnitude
            : -1f * Skhandler.rootVelocity.magnitude;

        animator.SetFloat("Forward", animSpeed, 0.1f, Time.deltaTime);
    }

    #region inspector vars

    [Header("IK SETTINGS")]
    [Tooltip(
        "Distance (between sole and environment under it) under which a foot is considered fully on the floor for IK calculation. Full foot IK application ratio will be applied.")]
    public float thresholdEnterGroundedState = .03f;

    [Tooltip(
        "Distance (between sole and environment under it) under which the IK will be gradually applied. Above this, no foot IK is applied.")]
    public float thresholdLeaveGroundedState = .2f;

    [Tooltip("Radius of movements filtered when the filterMovementsOnGround parameter is enabled.")]
    public float thresholdFootLock = .05f;

    [Tooltip("Layers detected as floor for the IK")]
    public LayerMask raycastDetectionLayers;

    [Tooltip("Coefficient of application of foot IK, position-wise.")] [Range(0f, 1f)]
    public float ikPositionApplicationRatio = 1f;

    [Tooltip("Coefficient of application of foot IK, rotation-wise.")] [Range(0f, 1f)]
    public float ikRotationApplicationRatio = 1f;

    // Coefficient of application of hint to bend legs in right direction.
    private readonly float ikHintWeight = 1f;

    [Header("RIG SETTINGS")] public float ankleHeightOffset = 0.102f;

    [Header("Keyboard controls")] public KeyCode resetAutomaticOffset = KeyCode.R;

    #endregion

    #region vars

    public SkeletonHandler Skhandler { get; set; } = null;

    public Vector3 RootHeightOffset { get; set; } = Vector3.zero;

    public ZEDBodyTrackingManager BodyTrackingManager { get; set; }

    /// Foot locking variables
    // If not on ground, no lock will be applied
    private bool groundedL;

    private bool groundedR;

    // SDK position to compare agianst for foot locking
    private Vector3 curPosAnkleL = Vector3.zero;
    private Vector3 curPosAnkleR = Vector3.zero;

    // If true, foot should not move
    private bool lockFootL;
    private bool lockFootR;

    #endregion


    /// --------------------------------------------
    /// ------------MonoBehaviour methods-----------
    /// --------------------------------------------

    #region MonoBehaviour Methods

    private void Awake() {
        if (!NetworkManager.Singleton.IsServer) {
            // destroy height offsetter if not server
            Destroy(GetComponent<HeightOffsetter>());
            // destroy animator if not server
            Destroy(GetComponent<Animator>());
            // destroy this script if not server
            Destroy(this);
        }

        BodyTrackingManager = FindObjectOfType<ZEDBodyTrackingManager>();
        if (BodyTrackingManager == null) {
            //Debug.LogError("ZEDManagerIK: No body tracking manager loaded!");
        }

        heightOffsetter = GetComponent<HeightOffsetter>();
    }

    private void Start() {
        animator = GetComponent<Animator>();
        HeadPositionUpdate positionUpdate = GetComponentInChildren<HeadPositionUpdate>();
        positionUpdate.head = animator.GetBoneTransform(HumanBodyBones.Head);
    }

    private void Update() {
        // reset automatic height offset calibration
        if (Input.GetKeyDown(resetAutomaticOffset)) heightOffsetter.CurrentheightOffset = 0;
    }

    #endregion

    /// --------------------------------------------
    /// -----------------Foot IK--------------------
    /// --------------------------------------------

    #region Foot IK

    private Vector3 curIKTargetPosL = Vector3.zero;

    private Vector3 curIKTargetPosR = Vector3.zero;
    private Quaternion curIKTargetRotL = Quaternion.identity;
    private Quaternion curIKTargetRotR = Quaternion.identity;
    private Vector3 targetFootLockL = Vector3.zero;
    private Vector3 targetFootLockR = Vector3.zero;

    /// <summary>
    ///     Checks the foot locking state if the feature is enabled and returns the point on ground to use as effector for the
    ///     IK.
    /// </summary>
    /// <param name="footLock">Foot locking state for this foot.</param>
    /// <param name="hitPoint">Hit position of the ray from the foot.</param>
    /// <param name="prevIKTargetPos">IK target on the previous frame. Should be replaced with the output of this function.</param>
    /// <param name="grounded">If the foot is close enough to the ground to have full ik application.</param>
    /// <param name="targetFootLock">Updated only if foot is unlocked. Target to lerp toward.</param>
    /// <returns>The effector target position</returns>
    private Vector3 FindIKTargetPosition(bool footLock, Vector3 hitPoint, Vector3 prevIKTargetPos, bool grounded,
        ref Vector3 targetFootLock) {
        var flsmooth = Mathf.Clamp(1 - BodyTrackingManager.footLockingSmoothingValue, 0, 1);
        if (BodyTrackingManager.EnableFootLocking) {
            if (footLock && grounded) {
                return Vector3.Lerp(prevIKTargetPos,
                    new Vector3(targetFootLock.x, hitPoint.y + ankleHeightOffset, targetFootLock.z),
                    flsmooth);
            }

            targetFootLock = hitPoint + new Vector3(0, ankleHeightOffset, 0);
            return Vector3.Lerp(
                prevIKTargetPos,
                hitPoint + new Vector3(0, ankleHeightOffset, 0),
                flsmooth);
        }

        return Vector3.Lerp(
            prevIKTargetPos,
            hitPoint + new Vector3(0, ankleHeightOffset, 0),
            flsmooth);
    }

    /// <summary>
    ///     Returns the target rotation for the IK. The goal it to have the foot flat depending on the normal found by the
    ///     raycast.
    /// </summary>
    /// <param name="hitNormal">Normal at raycast hit.</param>
    /// <param name="toePos">Toes position. Used to correctly orient the foot following its forward direction.</param>
    /// <param name="anklePos">Ankle position. Used to correctly orient the foot following its forward direction.</param>
    /// <returns></returns>
    private Quaternion FindIKTargetRotation(Vector3 hitNormal, Vector3 toePos, Vector3 anklePos,
        Quaternion rootOrientation, Quaternion curFootRotation) {
        var forward = Vector3.ProjectOnPlane(rootOrientation * (toePos - anklePos), Vector3.up);
        return Quaternion.Slerp(curFootRotation, Quaternion.LookRotation(forward, hitNormal),
            Mathf.Clamp(1 - BodyTrackingManager.footLockingSmoothingValue, 0, 1));
    }

    private Vector3 FindIKHintPosition(HumanBodyBones kneeBone) {
        var kneeT = animator.GetBoneTransform(kneeBone);
        var hintPos = kneeT.position + 0.4f * kneeT.forward;
        hintPos = animator.bodyRotation * hintPos;
        hintPos += animator.bodyPosition;
        return hintPos;
    }

    #endregion

    /// --------------------------------------------
    /// -----------------Utility Methods------------
    /// --------------------------------------------

    #region Utility methods

    /// <summary>
    ///     Computes and returns distance between the projections of <paramref name="vec1" /> and <paramref name="vec2" /> on
    ///     an horizontal plane
    /// </summary>
    private float HorizontalDist(Vector3 vec1, Vector3 vec2) {
        return Vector2.Distance(new Vector2(vec1.x, vec1.z), new Vector2(vec2.x, vec2.z));
    }

    /// <summary>
    ///     Gets a linear interpolation of the application ratio for the IK depending on the distance to the floor.
    ///     Used for smoothing the transition between grounded and not.
    /// </summary>
    /// <param name="d">Distance between sole and floor (not ankle and floor).</param>
    /// <param name="tMin">Threshold min: Distance above this value implies no application of IK.</param>
    /// <param name="tMax">Threshold max: Distance under this value implies full (depending on setting) application of IK.</param>
    /// <param name="rMax">Max ratio (default 1).</param>
    /// <returns>Ratio of IK application between 0 and rmax, depending on d, tmin and tmax./returns>
    private float GetLinearIKRatio(float d, float tMin, float tMax, float rMax = 1) {
        var ikr = Mathf.Min(1, Mathf.Max(0, rMax * (tMin - d) / (tMin - tMax)));
        return ikr;
    }

    /// <summary>
    ///     Get position of bone after application of bodyPosition and bodyRotation.
    /// </summary>
    /// <param name="bone"></param>
    private Vector3 GetWorldPosCurrentState(HumanBodyBones bone) {
        var ret = animator.GetBoneTransform(bone).position;
        ret = animator.bodyPosition + animator.bodyRotation * ret;
        return ret;
    }

    /// <summary>
    ///     Utility function to compute and apply the height offset from the height offset manager to the body.
    /// </summary>
    private void ManageHeightOffset(Vector3 posAnkleL, Vector3 posAnkleR, Vector3 hitPointL, Vector3 hitPointR) {
        RootHeightOffset =
            heightOffsetter.ComputeRootHeightOffsetFromRaycastInfo(posAnkleL, posAnkleR, hitPointL, hitPointR,
                ankleHeightOffset);
        if (animator != null)
            animator.bodyPosition += RootHeightOffset;
        else
            transform.position = Skhandler.TargetBodyPositionWithHipOffset + RootHeightOffset;
    }

    #endregion


    /// --------------------------------------------
    /// -----------------Main Pipeline--------------
    /// --------------------------------------------

    #region Main Pipeline

    /// <summary>
    ///     Applies the current local rotations of the rig to the animator.
    /// </summary>
    private void ApplyAllRigRotationsOnAnimator() {
        if (BodyTrackingManager.bodyMode == ZEDBodyTrackingManager.BODY_MODE.FULL_BODY)
            Skhandler.MoveAnimator(BodyTrackingManager.EnableSmoothing,
                Mathf.Clamp(1 - BodyTrackingManager.smoothingValue, 0, 1));
        else if (BodyTrackingManager.bodyMode == ZEDBodyTrackingManager.BODY_MODE.UPPER_BODY)
            Skhandler.MoveAnimatorUpperBody(BodyTrackingManager.EnableSmoothing,
                Mathf.Clamp(1 - BodyTrackingManager.smoothingValue, 0, 1));
    }

    /// <summary>
    ///     Raycast based on <paramref name="ankleLastFramePosL" /> and <paramref name="ankleLastFramePosR" />, the position of
    ///     the feet at the previous frame.
    ///     If the ray does not hit, the hitPoint are set to the position of the feet and the hitNormal to Vector3.up.
    /// </summary>
    private void RaycastManagementAnimator(
        out bool hitSuccessfulL, out bool hitSuccessfulR,
        out Vector3 hitPointL, out Vector3 hitPointR,
        out Vector3 hitNormalL, out Vector3 hitNormalR,
        Vector3 ankleLastFramePosL, Vector3 ankleLastFramePosR) {
        // Initialize vars
        hitPointL = ankleLastFramePosL;
        hitPointR = ankleLastFramePosR;
        hitNormalL = Vector3.up;
        hitNormalR = Vector3.up;

        var postStartRayL = GetWorldPosCurrentState(HumanBodyBones.LeftFoot);
        var postStartRayR = GetWorldPosCurrentState(HumanBodyBones.RightFoot);

        // Shoot a ray from 5m above the foot towards 5m under the foot
        var rayL = new Ray(postStartRayL + Vector3.up * 5, Vector3.down);
        hitSuccessfulL = Physics.Raycast(rayL, out var hitL, 10, raycastDetectionLayers);
        if (hitSuccessfulL) {
            hitPointL = hitL.point;
            hitNormalL = hitL.normal;
        }

        var rayR = new Ray(postStartRayR + Vector3.up * 5, Vector3.down);
        hitSuccessfulR = Physics.Raycast(rayR, out var hitR, 10, raycastDetectionLayers);
        if (hitSuccessfulR) {
            hitPointR = hitR.point;
            hitNormalR = hitR.normal;
        }
    }


    /// <summary>
    ///     Computes Foot IK.
    ///     1) Apply bones rotations to animator 2) Apply root position and rotations. 3) Do Foot IK.
    /// </summary>
    private void OnAnimatorIK() {
        if (Skhandler) {
            // 1) Update target positions and rotations.
            ApplyAllRigRotationsOnAnimator();

            // 2) Set root position/rotation
            animator.bodyPosition = BodyTrackingManager.bodyMode == ZEDBodyTrackingManager.BODY_MODE.FULL_BODY
                ? Skhandler.TargetBodyPositionWithHipOffset
                : new Vector3(Skhandler.TargetBodyPositionWithHipOffset.x, animator.bodyPosition.y,
                    Skhandler.TargetBodyPositionWithHipOffset.z);
            animator.bodyRotation = Skhandler.TargetBodyOrientationSmoothed;

            // Store raycast info data
            bool hitSuccessfulL;
            bool hitSuccessfulR;
            Vector3 hitPointL;
            Vector3 hitPointR;
            Vector3 hitNormalL;
            Vector3 hitNormalR;

            // Get raycast information from feet
            RaycastManagementAnimator(out hitSuccessfulL, out hitSuccessfulR, out hitPointL, out hitPointR,
                out hitNormalL, out hitNormalR, GetWorldPosCurrentState(HumanBodyBones.LeftFoot),
                GetWorldPosCurrentState(HumanBodyBones.RightFoot));

            // Manage Height Offset
            ManageHeightOffset(GetWorldPosCurrentState(HumanBodyBones.LeftFoot),
                GetWorldPosCurrentState(HumanBodyBones.RightFoot),
                hitPointL, hitPointR);

            if (BodyTrackingManager.EnableFootIK) {
                /// ----------------------------------------------------------------
                /// Left Foot ------------------------------------------------------
                /// ----------------------------------------------------------------
                animator.SetIKHintPosition(AvatarIKHint.LeftKnee, FindIKHintPosition(HumanBodyBones.LeftLowerLeg));
                animator.SetIKHintPositionWeight(AvatarIKHint.LeftKnee, ikHintWeight);

                // Find the effector position & rotation for the IK
                curIKTargetPosL =
                    FindIKTargetPosition(lockFootL, hitPointL, curIKTargetPosL, groundedL, ref targetFootLockL);
                curIKTargetRotL = FindIKTargetRotation(hitNormalL,
                    animator.GetBoneTransform(HumanBodyBones.LeftToes).position,
                    animator.GetBoneTransform(HumanBodyBones.LeftFoot).position,
                    Skhandler.TargetBodyOrientationSmoothed,
                    curIKTargetRotL);

                // Set effectors and application ratios
                if (hitSuccessfulL) {
                    animator.SetIKPosition(AvatarIKGoal.LeftFoot, curIKTargetPosL);
                    animator.SetIKRotation(AvatarIKGoal.LeftFoot, curIKTargetRotL);
                    // distance from sole to floor
                    var dist = Mathf.Abs(hitPointL.y - GetWorldPosCurrentState(HumanBodyBones.LeftFoot).y +
                                         ankleHeightOffset);
                    groundedL = dist <= thresholdEnterGroundedState;
                    var ikRatioPos = GetLinearIKRatio(dist, thresholdLeaveGroundedState, thresholdEnterGroundedState,
                        ikPositionApplicationRatio);
                    var ikRatioRot = GetLinearIKRatio(dist, thresholdLeaveGroundedState, thresholdEnterGroundedState,
                        ikRotationApplicationRatio);
                    animator.SetIKRotationWeight(AvatarIKGoal.LeftFoot, ikRatioRot);
                    animator.SetIKPositionWeight(AvatarIKGoal.LeftFoot, ikRatioPos);
                }
                else // set application ratios to 0
                {
                    groundedL = false;
                    animator.SetIKPosition(AvatarIKGoal.LeftFoot, curIKTargetPosL);
                    animator.SetIKRotation(AvatarIKGoal.LeftFoot, curIKTargetRotL);
                    animator.SetIKRotationWeight(AvatarIKGoal.LeftFoot, 0);
                    animator.SetIKPositionWeight(AvatarIKGoal.LeftFoot, 0);
                }

                /// ----------------------------------------------------------------
                /// Right Foot -----------------------------------------------------
                /// ----------------------------------------------------------------

                animator.SetIKHintPosition(AvatarIKHint.RightKnee, FindIKHintPosition(HumanBodyBones.RightLowerLeg));
                animator.SetIKHintPositionWeight(AvatarIKHint.RightKnee, ikHintWeight);

                // Find the effector position & rotation for the IK
                curIKTargetPosR =
                    FindIKTargetPosition(lockFootR, hitPointR, curIKTargetPosR, groundedR, ref targetFootLockR);
                curIKTargetRotR = FindIKTargetRotation(hitNormalR,
                    animator.GetBoneTransform(HumanBodyBones.RightToes).position,
                    animator.GetBoneTransform(HumanBodyBones.RightFoot).position,
                    Skhandler.TargetBodyOrientationSmoothed,
                    curIKTargetRotR);

                // Set effectors and application ratios
                if (hitSuccessfulR) {
                    animator.SetIKPosition(AvatarIKGoal.RightFoot, curIKTargetPosR);
                    animator.SetIKRotation(AvatarIKGoal.RightFoot, curIKTargetRotR);
                    // distance from sole to floor
                    var dist = Mathf.Abs(hitPointR.y - GetWorldPosCurrentState(HumanBodyBones.RightFoot).y +
                                         ankleHeightOffset);
                    groundedR = dist <= thresholdEnterGroundedState;
                    var ikRatioPos = GetLinearIKRatio(dist, thresholdLeaveGroundedState, thresholdEnterGroundedState,
                        ikPositionApplicationRatio);
                    var ikRatioRot = GetLinearIKRatio(dist, thresholdLeaveGroundedState, thresholdEnterGroundedState,
                        ikRotationApplicationRatio);
                    animator.SetIKRotationWeight(AvatarIKGoal.RightFoot, ikRatioRot);
                    animator.SetIKPositionWeight(AvatarIKGoal.RightFoot, ikRatioPos);
                }
                else // set application ratios to 0
                {
                    groundedR = false;
                    animator.SetIKPosition(AvatarIKGoal.RightFoot, curIKTargetPosR);
                    animator.SetIKRotation(AvatarIKGoal.RightFoot, curIKTargetRotR);
                    animator.SetIKRotationWeight(AvatarIKGoal.RightFoot, 0);
                    animator.SetIKPositionWeight(AvatarIKGoal.RightFoot, 0);
                }
            }

            //if the IK is not active, disable ik hints/effectors
            else {
                animator.SetIKPositionWeight(AvatarIKGoal.RightFoot, 0);
                animator.SetIKRotationWeight(AvatarIKGoal.RightFoot, 0);
                animator.SetIKPositionWeight(AvatarIKGoal.LeftFoot, 0);
                animator.SetIKRotationWeight(AvatarIKGoal.LeftFoot, 0);
                animator.SetIKHintPositionWeight(AvatarIKHint.RightKnee, 0);
                animator.SetIKHintPositionWeight(AvatarIKHint.LeftKnee, 0);
            }
        }
    }

    #endregion
}