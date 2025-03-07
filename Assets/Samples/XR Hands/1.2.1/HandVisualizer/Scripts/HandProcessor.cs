using System.Collections.Generic;
using UnityEngine.XR.Hands.Processing;

namespace UnityEngine.XR.Hands.Samples.VisualizerSample {
    // Example hand processor that applies transformations on the root poses to
    // modify the hands skeleton. Note it is possible to modify the bones
    // directly for more advanced use cases that are not shown here.
    public class HandProcessor : MonoBehaviour, IXRHandProcessor {
        public enum ProcessorExampleMode {
            None,
            Smoothing,
            Invert
        }

        private static readonly List<XRHandSubsystem> s_SubsystemsReuse = new();

        [SerializeField] [Tooltip("The mode to use for the sample processor.")]
        private ProcessorExampleMode m_ProcessorExampleMode = ProcessorExampleMode.Smoothing;

        // Smoothing factors for the left and right hands.
        [Header("Smoothing parameters")]
        [SerializeField]
        [Tooltip(
            "The smoothing factor to use when smoothing the root of the left hand in the sample processor. Use 0 for no smoothing.")]
        private float m_LeftHandSmoothingFactor = 16f;

        [SerializeField]
        [Tooltip(
            "The smoothing factor to use when smoothing the root of the right hand in the sample processor. Use 0 for no smoothing.")]
        private float m_RightHandSmoothingFactor = 16f;

        // Variables used for smoothing hand movements.
        private readonly bool m_FirstFrame = false;
        private Vector3 m_LastLeftHandPosition;
        private ProcessorExampleMode m_LastProcessorExampleMode = ProcessorExampleMode.None;
        private Vector3 m_LastRightHandPosition;
        private Pose m_LeftHandPose = Pose.identity;
        private Pose m_RightHandPose = Pose.identity;

        private XRHandSubsystem m_Subsystem;

        public ProcessorExampleMode processorExampleMode {
            get => m_ProcessorExampleMode;
            set => m_ProcessorExampleMode = value;
        }

        private void Update() {
            if (m_Subsystem != null)
                return;

            SubsystemManager.GetSubsystems(s_SubsystemsReuse);
            if (s_SubsystemsReuse.Count == 0)
                return;

            m_Subsystem = s_SubsystemsReuse[0];
            m_Subsystem.RegisterProcessor(this);
        }

        private void OnDisable() {
            if (m_Subsystem != null) {
                m_Subsystem.UnregisterProcessor(this);
                m_Subsystem = null;
            }
        }

        public int callbackOrder => 0;

        public void ProcessJoints(XRHandSubsystem subsystem, XRHandSubsystem.UpdateSuccessFlags successFlags,
            XRHandSubsystem.UpdateType updateType) {
            switch (m_ProcessorExampleMode) {
                case ProcessorExampleMode.Smoothing:
                    SmoothHandsExample(subsystem, successFlags, updateType,
                        m_LastProcessorExampleMode != m_ProcessorExampleMode);
                    break;

                case ProcessorExampleMode.Invert:
                    InvertHandsExample(subsystem, successFlags, updateType);
                    break;
            }

            m_LastProcessorExampleMode = m_ProcessorExampleMode;
        }

        // Smooths the hand movements of an XRHandSubsystem by updating the root
        // pose of the left and right hands with interpolated positions.
        private void SmoothHandsExample(XRHandSubsystem subsystem, XRHandSubsystem.UpdateSuccessFlags successFlags,
            XRHandSubsystem.UpdateType updateType, bool modeChanged) {
            var leftHand = subsystem.leftHand;
            var rightHand = subsystem.rightHand;

            if (leftHand.isTracked && m_LeftHandSmoothingFactor > 0) {
                var leftPose = leftHand.rootPose;
                var currentLeftHandPosition = leftPose.position;
                if (!m_FirstFrame && !modeChanged) {
                    var tweenAmt = Time.deltaTime * m_LeftHandSmoothingFactor;
                    currentLeftHandPosition = Vector3.Lerp(m_LastLeftHandPosition, currentLeftHandPosition, tweenAmt);
                    m_LeftHandPose.position = currentLeftHandPosition;
                    m_LeftHandPose.rotation = leftPose.rotation;

                    leftHand.SetRootPose(m_LeftHandPose);
                    subsystem.SetCorrespondingHand(leftHand);
                }

                m_LastLeftHandPosition = currentLeftHandPosition;
            }

            if (rightHand.isTracked && m_RightHandSmoothingFactor > 0) {
                var rightPose = rightHand.rootPose;
                var currentRightHandPosition = rightPose.position;
                if (!m_FirstFrame && !modeChanged) {
                    var tweenAmt = Time.deltaTime * m_RightHandSmoothingFactor;
                    currentRightHandPosition =
                        Vector3.Lerp(m_LastRightHandPosition, currentRightHandPosition, tweenAmt);
                    m_RightHandPose.position = currentRightHandPosition;
                    m_RightHandPose.rotation = rightPose.rotation;

                    rightHand.SetRootPose(m_RightHandPose);
                    subsystem.SetCorrespondingHand(rightHand);
                }

                m_LastRightHandPosition = currentRightHandPosition;
            }
        }

        // Call this from process joints to try inverting the user's hands.
        private void InvertHandsExample(XRHandSubsystem subsystem, XRHandSubsystem.UpdateSuccessFlags successFlags,
            XRHandSubsystem.UpdateType updateType) {
            var leftHand = subsystem.leftHand;
            var leftHandPose = leftHand.rootPose;

            var rightHand = subsystem.rightHand;
            var rightHandPose = rightHand.rootPose;

            if (leftHand.isTracked) {
                leftHand.SetRootPose(rightHandPose);
                subsystem.SetCorrespondingHand(leftHand);

                rightHand.SetRootPose(leftHandPose);
                subsystem.SetCorrespondingHand(rightHand);
            }
        }
    }
}