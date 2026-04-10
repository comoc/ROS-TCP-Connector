using System;
using Unity.Robotics.ROSTCPConnector.MessageGeneration;

namespace RosMessageTypes.ActionTutorialsInterfaces
{
    [Serializable]
    public class FibonacciGoal : Message
    {
        public const string k_RosMessageName = "action_tutorials_interfaces/Fibonacci_Goal";
        public override string RosMessageName => k_RosMessageName;

        public int order;

        public FibonacciGoal()
        {
            this.order = 0;
        }

        public FibonacciGoal(int order)
        {
            this.order = order;
        }

        public static FibonacciGoal Deserialize(MessageDeserializer deserializer) => new FibonacciGoal(deserializer);

        private FibonacciGoal(MessageDeserializer deserializer)
        {
            deserializer.Read(out this.order);
        }

        public override void SerializeTo(MessageSerializer serializer)
        {
            serializer.Write(this.order);
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
