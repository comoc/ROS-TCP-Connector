using System;
using Unity.Robotics.ROSTCPConnector.MessageGeneration;

namespace RosMessageTypes.ActionTutorialsInterfaces
{
    [Serializable]
    public class FibonacciFeedback : Message
    {
        public const string k_RosMessageName = "action_tutorials_interfaces/Fibonacci_Feedback";
        public override string RosMessageName => k_RosMessageName;

        public int[] partial_sequence;

        public FibonacciFeedback()
        {
            this.partial_sequence = new int[0];
        }

        public FibonacciFeedback(int[] partial_sequence)
        {
            this.partial_sequence = partial_sequence;
        }

        public static FibonacciFeedback Deserialize(MessageDeserializer deserializer) => new FibonacciFeedback(deserializer);

        private FibonacciFeedback(MessageDeserializer deserializer)
        {
            deserializer.Read(out this.partial_sequence, sizeof(int), deserializer.ReadLength());
        }

        public override void SerializeTo(MessageSerializer serializer)
        {
            serializer.WriteLength(this.partial_sequence);
            serializer.Write(this.partial_sequence);
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#else
        [UnityEngine.RuntimeInitializeOnLoadMethod]
#endif
        public static void Register()
        {
            MessageRegistry.Register(k_RosMessageName, Deserialize);
        }
    }
}
