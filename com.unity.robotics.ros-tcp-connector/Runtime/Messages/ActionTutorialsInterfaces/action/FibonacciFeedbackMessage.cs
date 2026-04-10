using System;
using Unity.Robotics.ROSTCPConnector.MessageGeneration;

namespace RosMessageTypes.ActionTutorialsInterfaces
{
    /// <summary>
    /// The wire-level FeedbackMessage for Fibonacci actions.
    /// Layout: unique_identifier_msgs/UUID goal_id + Fibonacci_Feedback feedback.
    /// </summary>
    [Serializable]
    public class FibonacciFeedbackMessage : Message
    {
        public const string k_RosMessageName = "action_tutorials_interfaces/Fibonacci_FeedbackMessage";
        // The endpoint may also report this type with /action/ in the path
        // when it refreshes the topic list from the ROS2 graph.
        public const string k_RosMessageNameAlt = "action_tutorials_interfaces/action/Fibonacci_FeedbackMessage";
        public override string RosMessageName => k_RosMessageName;

        public byte[] goal_id = new byte[16];
        public FibonacciFeedback feedback = new FibonacciFeedback();

        public FibonacciFeedbackMessage() { }

        public static FibonacciFeedbackMessage Deserialize(MessageDeserializer deserializer)
            => new FibonacciFeedbackMessage(deserializer);

        private FibonacciFeedbackMessage(MessageDeserializer deserializer)
        {
            // UUID is uint8[16] — 16 raw bytes, no length prefix.
            goal_id = new byte[16];
            for (int i = 0; i < 16; i++)
                deserializer.Read(out goal_id[i]);

            // Feedback body follows inline.
            feedback = FibonacciFeedback.Deserialize(deserializer);
        }

        public override void SerializeTo(MessageSerializer serializer)
        {
            serializer.Write(goal_id);
            feedback.SerializeTo(serializer);
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#else
        [UnityEngine.RuntimeInitializeOnLoadMethod]
#endif
        public static void Register()
        {
            MessageRegistry.Register(k_RosMessageName, Deserialize);
            MessageRegistry.Register(k_RosMessageNameAlt, Deserialize);
        }
    }
}
