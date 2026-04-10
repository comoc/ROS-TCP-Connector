using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Robotics.ROSTCPConnector.MessageGeneration;
using UnityEngine;

namespace Unity.Robotics.ROSTCPConnector
{
    /// <summary>
    /// Client-side handle for a ROS2 Action. Sends goals, receives feedback,
    /// awaits results, and cancels goals through the patched ROS-TCP-Endpoint.
    /// Requires the comoc/ROS-TCP-Endpoint fork (main-ros2 branch).
    /// </summary>
    public class ROSActionClient<TGoal, TResult, TFeedback>
        where TGoal : Message, new()
        where TResult : Message, new()
        where TFeedback : Message, new()
    {
        readonly ROSConnection m_Connection;
        readonly string m_ActionName;
        readonly string m_ActionType;
        bool m_Registered;

        readonly Dictionary<string, ActionGoalHandle<TResult, TFeedback>> m_GoalHandles =
            new Dictionary<string, ActionGoalHandle<TResult, TFeedback>>();

        public ROSActionClient(ROSConnection connection, string actionName, string actionType)
        {
            m_Connection = connection;
            m_ActionName = actionName;
            m_ActionType = actionType;
        }

        public async Task<ActionGoalHandle<TResult, TFeedback>> SendGoal(TGoal goal)
        {
            EnsureRegistered();

            var (srvId, pauser) = m_Connection.AllocateServiceRequest();

            m_Connection.QueueSysCommand(
                SysCommand.k_SysCommand_ActionSendGoal,
                new SysCommand_ActionGoalOp { action_name = m_ActionName, srv_id = srvId });
            m_Connection.QueueRawMessage(m_ActionName, goal);

            byte[] responseBytes = (byte[])await pauser.PauseUntilResumed();

            if (responseBytes == null || responseBytes.Length < 16)
            {
                Debug.LogWarning($"ROSActionClient: invalid send_goal response for {m_ActionName}");
                return null;
            }

            byte[] goalId = new byte[16];
            Array.Copy(responseBytes, 0, goalId, 0, 16);

            // CDR header (4 bytes) + bool accepted (1 byte) starts at offset 16
            byte[] cdrResponse = new byte[responseBytes.Length - 16];
            Array.Copy(responseBytes, 16, cdrResponse, 0, cdrResponse.Length);
            bool accepted = cdrResponse.Length > 4 && cdrResponse[4] != 0;

            var handle = new ActionGoalHandle<TResult, TFeedback>(
                m_Connection, m_ActionName, goalId, accepted);

            if (accepted)
                m_GoalHandles[BytesToHex(goalId)] = handle;

            return handle;
        }

        internal void OnFeedbackReceived(byte[] goalIdBytes, TFeedback feedback)
        {
            string key = BytesToHex(goalIdBytes);
            if (m_GoalHandles.TryGetValue(key, out var handle))
                handle.InvokeFeedback(feedback);
        }

        void EnsureRegistered()
        {
            if (m_Registered)
                return;
            m_Registered = true;

            m_Connection.QueueSysCommand(
                SysCommand.k_SysCommand_ActionClient,
                new SysCommand_ActionRegistration
                {
                    action_name = m_ActionName,
                    action_type = m_ActionType
                });
        }

        /// <summary>
        /// Subscribe to the feedback topic using a FeedbackMessage type that
        /// contains both goal_id (byte[16]) and feedback (TFeedback).
        /// Call this after creating the client to enable FeedbackReceived
        /// events on goal handles.
        ///
        /// Example:
        ///   client.RegisterFeedbackSubscription&lt;FibonacciFeedbackMessage&gt;(
        ///       msg => msg.goal_id, msg => msg.feedback);
        /// </summary>
        public void RegisterFeedbackSubscription<TFeedbackMessage>(
            Func<TFeedbackMessage, byte[]> getGoalId,
            Func<TFeedbackMessage, TFeedback> getFeedback)
            where TFeedbackMessage : Message, new()
        {
            string feedbackTopic = m_ActionName + "/_action/feedback";
            // Use the short type name (without /action/) for the __subscribe
            // syscommand — this is what the endpoint uses to resolve the type.
            // The endpoint sends feedback with this short name, but __topic_list
            // reports the full /action/ path, causing a type mismatch in
            // Subscribe<T>. Using SubscribeByMessageName with the short name
            // avoids the generic type check.
            string feedbackTypeName = m_ActionType + "_FeedbackMessage";
            m_Connection.SubscribeByMessageName(feedbackTopic, feedbackTypeName, (Message msg) =>
            {
                if (msg is TFeedbackMessage typedMsg)
                {
                    byte[] goalId = getGoalId(typedMsg);
                    TFeedback feedback = getFeedback(typedMsg);
                    OnFeedbackReceived(goalId, feedback);
                }
            });
        }

        static string BytesToHex(byte[] bytes)
        {
            return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        }
    }

    /// <summary>
    /// Handle to a single in-flight action goal.
    /// </summary>
    public class ActionGoalHandle<TResult, TFeedback>
        where TResult : Message, new()
        where TFeedback : Message, new()
    {
        readonly ROSConnection m_Connection;
        readonly string m_ActionName;

        public byte[] GoalId { get; }
        public bool Accepted { get; }
        public event Action<TFeedback> FeedbackReceived;

        internal ActionGoalHandle(ROSConnection connection, string actionName,
            byte[] goalId, bool accepted)
        {
            m_Connection = connection;
            m_ActionName = actionName;
            GoalId = goalId;
            Accepted = accepted;
        }

        public async Task<TResult> GetResult()
        {
            var (srvId, pauser) = m_Connection.AllocateServiceRequest();

            m_Connection.QueueSysCommand(
                SysCommand.k_SysCommand_ActionGetResult,
                new SysCommand_ActionGoalOp { action_name = m_ActionName, srv_id = srvId });

            var request = new GetResultRequestProxy { goal_id = GoalId };
            m_Connection.QueueRawMessage(m_ActionName, request);

            byte[] responseBytes = (byte[])await pauser.PauseUntilResumed();

            if (responseBytes == null || responseBytes.Length == 0)
                return default;

            // GetResult_Response CDR layout:
            //   [4 bytes CDR header (00 01 00 00)]
            //   [1 byte int8 status]
            //   [3 bytes alignment padding to 4-byte boundary]
            //   [Result CDR body (WITHOUT its own CDR header)]
            //
            // We need to strip the CDR header + status + padding, then
            // prepend a fresh CDR header so the deserializer sees a
            // well-formed CDR stream for TResult.
            const int headerSize = 4;  // CDR encapsulation header
            const int statusSize = 1;  // int8 status
            const int padSize = 3;     // alignment to 4-byte boundary
            int skipBytes = headerSize + statusSize + padSize;

            if (responseBytes.Length <= skipBytes)
                return default;

            // Build a new byte array: CDR header + Result body
            byte[] resultCdr = new byte[4 + (responseBytes.Length - skipBytes)];
            resultCdr[0] = 0x00; resultCdr[1] = 0x01; // CDR_LE
            resultCdr[2] = 0x00; resultCdr[3] = 0x00;
            Array.Copy(responseBytes, skipBytes, resultCdr, 4,
                responseBytes.Length - skipBytes);

            var deserializer = new MessageDeserializer();
            return deserializer.DeserializeMessage<TResult>(resultCdr);
        }

        public async Task<bool> Cancel()
        {
            var (srvId, pauser) = m_Connection.AllocateServiceRequest();

            m_Connection.QueueSysCommand(
                SysCommand.k_SysCommand_ActionCancelGoal,
                new SysCommand_ActionGoalOp { action_name = m_ActionName, srv_id = srvId });

            var request = new CancelGoalRequestProxy { goal_id = GoalId };
            m_Connection.QueueRawMessage(m_ActionName, request);

            byte[] responseBytes = (byte[])await pauser.PauseUntilResumed();
            if (responseBytes == null || responseBytes.Length < 5)
                return false;

            // CDR header (4) + int8 return_code. ERROR_NONE = 0.
            return responseBytes[4] == 0;
        }

        internal void InvokeFeedback(TFeedback feedback)
        {
            FeedbackReceived?.Invoke(feedback);
        }
    }

    internal class GetResultRequestProxy : Message
    {
        public byte[] goal_id = new byte[16];
        public override string RosMessageName => "";

        public override void SerializeTo(MessageSerializer serializer)
        {
            serializer.Write(goal_id);
        }
    }

    internal class CancelGoalRequestProxy : Message
    {
        public byte[] goal_id = new byte[16];
        public override string RosMessageName => "action_msgs/CancelGoal";

        public override void SerializeTo(MessageSerializer serializer)
        {
            serializer.Write(goal_id);
            serializer.Write(0);        // stamp.sec
            serializer.Write((uint)0);  // stamp.nanosec
        }
    }
}
