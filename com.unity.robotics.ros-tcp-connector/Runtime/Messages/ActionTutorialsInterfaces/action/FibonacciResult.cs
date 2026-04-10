using System;
using Unity.Robotics.ROSTCPConnector.MessageGeneration;

namespace RosMessageTypes.ActionTutorialsInterfaces
{
    [Serializable]
    public class FibonacciResult : Message
    {
        public const string k_RosMessageName = "action_tutorials_interfaces/Fibonacci_Result";
        public override string RosMessageName => k_RosMessageName;

        public int[] sequence;

        public FibonacciResult()
        {
            this.sequence = new int[0];
        }

        public FibonacciResult(int[] sequence)
        {
            this.sequence = sequence;
        }

        public static FibonacciResult Deserialize(MessageDeserializer deserializer) => new FibonacciResult(deserializer);

        private FibonacciResult(MessageDeserializer deserializer)
        {
            deserializer.Read(out this.sequence, sizeof(int), deserializer.ReadLength());
        }

        public override void SerializeTo(MessageSerializer serializer)
        {
            serializer.WriteLength(this.sequence);
            serializer.Write(this.sequence);
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
