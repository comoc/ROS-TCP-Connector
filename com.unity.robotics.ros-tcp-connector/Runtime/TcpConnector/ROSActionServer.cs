using System;
using System.Collections.Generic;
using Unity.Robotics.ROSTCPConnector.MessageGeneration;
using UnityEngine;

namespace Unity.Robotics.ROSTCPConnector
{
    /// <summary>
    /// Server-side handle for a ROS2 Action implemented in Unity.
    /// When a ROS2 action client sends a goal, the endpoint forwards it
    /// to Unity via __request/__response. Unity processes the goal,
    /// publishes feedback, and sends the result back.
    ///
    /// Requires the comoc/ROS-TCP-Endpoint fork (main-ros2 branch).
    /// </summary>
    public class ROSActionServer<TGoal, TResult, TFeedback>
        where TGoal : Message, new()
        where TResult : Message, new()
        where TFeedback : Message, new()
    {
        readonly ROSConnection m_Connection;
        readonly string m_ActionName;
        readonly string m_ActionType;
        bool m_Registered;

        /// <summary>
        /// Fired when a ROS2 action client sends a goal. The handler
        /// receives the goal and a handle to publish feedback and set
        /// the result.
        /// </summary>
        public event Action<TGoal, ActionServerGoalHandle<TResult, TFeedback>> GoalReceived;

        // Active goals keyed by hex UUID.
        readonly Dictionary<string, ActiveGoal> m_ActiveGoals =
            new Dictionary<string, ActiveGoal>();

        struct ActiveGoal
        {
            public int SrvId;
            public ActionServerGoalHandle<TResult, TFeedback> Handle;
        }

        public ROSActionServer(ROSConnection connection, string actionName, string actionType)
        {
            m_Connection = connection;
            m_ActionName = actionName;
            m_ActionType = actionType;
        }

        /// <summary>
        /// Register the action server with the endpoint. Called
        /// automatically by ROSConnection.CreateActionServer, but can
        /// also be called manually if needed.
        /// </summary>
        public void EnsureRegistered()
        {
            if (m_Registered)
                return;
            m_Registered = true;

            m_Connection.QueueSysCommand(
                SysCommand.k_SysCommand_ActionServer,
                new SysCommand_ActionRegistration
                {
                    action_name = m_ActionName,
                    action_type = m_ActionType
                });
        }

        /// <summary>
        /// Called by ROSConnection when a __request arrives whose
        /// destination matches this action's name. The payload is
        /// 16-byte UUID + CDR Goal body.
        /// </summary>
        internal void OnGoalRequest(int srvId, byte[] payload)
        {
            if (payload == null || payload.Length < 16)
            {
                Debug.LogWarning($"ROSActionServer: invalid goal payload for {m_ActionName}");
                return;
            }

            byte[] goalId = new byte[16];
            Array.Copy(payload, 0, goalId, 0, 16);

            byte[] goalCdr = new byte[payload.Length - 16];
            Array.Copy(payload, 16, goalCdr, 0, goalCdr.Length);

            // Deserialize the Goal body.
            var deserializer = new MessageDeserializer();
            TGoal goal = deserializer.DeserializeMessage<TGoal>(goalCdr);

            var handle = new ActionServerGoalHandle<TResult, TFeedback>(
                m_Connection, m_ActionName, goalId, srvId);

            string key = BytesToHex(goalId);
            m_ActiveGoals[key] = new ActiveGoal { SrvId = srvId, Handle = handle };

            GoalReceived?.Invoke(goal, handle);
        }

        /// <summary>
        /// Remove a goal from the active set after result is sent.
        /// Called by ActionServerGoalHandle.SetResult.
        /// </summary>
        internal void RemoveGoal(byte[] goalId)
        {
            m_ActiveGoals.Remove(BytesToHex(goalId));
        }

        static string BytesToHex(byte[] bytes)
        {
            return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        }
    }

    /// <summary>
    /// Handle for a single goal being executed by Unity.
    /// </summary>
    public class ActionServerGoalHandle<TResult, TFeedback>
        where TResult : Message, new()
        where TFeedback : Message, new()
    {
        readonly ROSConnection m_Connection;
        readonly string m_ActionName;
        readonly int m_SrvId;

        public byte[] GoalId { get; }

        internal ActionServerGoalHandle(ROSConnection connection, string actionName,
            byte[] goalId, int srvId)
        {
            m_Connection = connection;
            m_ActionName = actionName;
            GoalId = goalId;
            m_SrvId = srvId;
        }

        /// <summary>
        /// Publish feedback for this goal. The endpoint forwards it
        /// to the ROS2 action client via goal_handle.publish_feedback().
        /// </summary>
        public void PublishFeedback(TFeedback feedback)
        {
            string uuidHex = BitConverter.ToString(GoalId).Replace("-", "").ToLowerInvariant();

            m_Connection.QueueSysCommand(
                SysCommand.k_SysCommand_ActionPublishFeedback,
                new SysCommand_ActionFeedback
                {
                    action_name = m_ActionName,
                    goal_uuid_hex = uuidHex
                });
            m_Connection.QueueRawMessage(m_ActionName, feedback);
        }

        /// <summary>
        /// Set the final result and complete this goal. The endpoint's
        /// execute_callback unblocks, calls goal_handle.succeed(),
        /// and returns the result to the ROS2 action client.
        /// </summary>
        public void SetResult(TResult result)
        {
            // Send __response{srv_id} + CDR Result body.
            // QueueSysCommand is public on ROSConnection.
            m_Connection.QueueSysCommand(
                SysCommand.k_SysCommand_ServiceResponse,
                new SysCommand_Service { srv_id = m_SrvId });
            m_Connection.QueueRawMessage(m_ActionName, result);
        }
    }
}
