﻿using System;
using System.Collections.Generic;
using MultiplayerTopDownTank.Messages;
using WaveEngine.Common.Graphics;
using WaveEngine.Common.Math;
using WaveEngine.Components.Cameras;
using WaveEngine.Components.Graphics2D;
using WaveEngine.Components.UI;
using WaveEngine.Framework;
using WaveEngine.Framework.Graphics;
using WaveEngine.Framework.Services;
using WaveEngine.Framework.UI;
using WaveEngine.Networking;
using WaveEngine.Networking.Messages;

namespace MultiplayerTopDownTank.Scenes
{
    public class LobbyScene : Scene
    {
        private const int MinIndex = 1;
        private const int MaxIndex = 4;
        private readonly List<int> assignedPlayerIndex;

        private TextBlock messageTextBlock;

        private readonly NetworkService networkService;

        public LobbyScene()
        {
            this.networkService = WaveServices.GetService<NetworkService>();
            this.networkService.HostConnected += this.OnHostConnected;
            this.networkService.MessageReceivedFromHost += this.ClientMessageReceived;
            this.networkService.MessageReceivedFromClient += this.HostMessageReceived;

            assignedPlayerIndex = new List<int>();
        }

        private void OnHostConnected(object sender, NetworkEndpoint endpoint)
        {
            this.SelectPlayer(WaveServices.Random.Next(MinIndex, MaxIndex));
        }

        protected override void CreateScene()
        {
            this.Load(WaveContent.Scenes.LobbyScene);

            var camera2D = new FixedCamera2D("Camera2D")
            {
                BackgroundColor = Color.CornflowerBlue
            };

            this.EntityManager.Add(camera2D);

            this.messageTextBlock = new TextBlock()
            {
                Width = 600,
                Height = 50,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(10, 0, 10, 10)
            };
            this.EntityManager.Add(this.messageTextBlock);

            messageTextBlock.Text = "Waiting player assignment...";
        }

        private void SelectPlayer(int playerIndex)
        {
            var message = NetworkMessageHelper.CreateMessage(this.networkService, NetworkAgentEnum.Client, NetworkCommandEnum.CreatePlayer, this.networkService.ClientIdentifier,
                playerIndex.ToString());
            this.networkService.SendToServer(message, DeliveryMethod.ReliableUnordered);
        }

        private int AssignPlayerIndex(int playerIndex)
        {
            lock (this)
            {
                if (this.assignedPlayerIndex.Contains(playerIndex))
                {
                    playerIndex = this.GetNextPlayerIndex();
                }

                this.assignedPlayerIndex.Add(playerIndex);
            }

            return playerIndex;
        }

        private int GetNextPlayerIndex()
        {
            for (int i = MinIndex; i <= MaxIndex; i++)
            {
                if (!this.assignedPlayerIndex.Contains(i))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Handles the messages received from the clients. Only when this player is the host.
        /// </summary>
        private void HostMessageReceived(object sender, NetworkEndpoint networkEndpoint, IncomingMessage receivedMessage)
        {
            // Get message from Server (Player must be created)
            NetworkCommandEnum command;
            string playerIdentifier;
            string playerIndex;
            NetworkMessageHelper.ReadMessage(receivedMessage, out command, out playerIdentifier, out playerIndex);

            // Send to other players to create theis foes.
            var resultPlayerIndex = this.AssignPlayerIndex(Convert.ToInt32(playerIndex));
            var sendToPlayersMessage = NetworkMessageHelper.CreateMessage(this.networkService, NetworkAgentEnum.Server, NetworkCommandEnum.CreatePlayer, playerIdentifier, resultPlayerIndex.ToString());
            this.networkService.SendToClients(sendToPlayersMessage, DeliveryMethod.ReliableUnordered);
        }

        /// <summary>
        /// Handles the messages received from the host.
        /// </summary>
        private void ClientMessageReceived(object sender, NetworkEndpoint networkEndpoint, IncomingMessage receivedMessage)
        {
            NetworkCommandEnum command;
            string playerIdentifier;
            string playerIndex;
            NetworkMessageHelper.ReadMessage(receivedMessage, out command, out playerIdentifier, out playerIndex);

            switch (command)
            {
                case NetworkCommandEnum.CreatePlayer:
                    if (this.networkService.ClientIdentifier == playerIdentifier)
                    {
                        this.HandlePlayerSelectionResponse(Convert.ToInt32(playerIndex));
                    }
                    break;
                default:
                    break;
            }
        }

        /// <summary>
        /// Handles the player selection response.
        /// </summary>
        /// <param name="playerIndex">Index of the player.</param>
        private void HandlePlayerSelectionResponse(int playerIndex)
        {
            if (playerIndex < 0)
            {
                this.ServerCompleted();
            }
            else
            {
                this.PlayerSelected(playerIndex);
            }
        }

        protected override void End()
        {
            base.End();

            this.networkService.MessageReceivedFromHost -= this.ClientMessageReceived;
            this.networkService.MessageReceivedFromClient -= this.HostMessageReceived;
        }

        private void PlayerSelected(int playerIndex)
        {
            // Wait 3 seconds and start game.
            int remainingSeconds = 3;
            var timerName = "PlayerSelectedTimer";
            this.UpdateRemainingSeconds(remainingSeconds);
            WaveServices.TimerFactory.CreateTimer(timerName, TimeSpan.FromSeconds(1), () =>
            {
                remainingSeconds--;
                if (remainingSeconds == 0)
                {
                    WaveServices.TimerFactory.RemoveTimer(timerName);

                    // Navigate to GameScene and created player with selected sprite.
                    WaveServices.ScreenContextManager.Push(new ScreenContext(new GameScene(playerIndex)));
                }

                this.UpdateRemainingSeconds(remainingSeconds);
            }, true, this);
        }

        private void UpdateRemainingSeconds(int remainingSeconds)
        {
            messageTextBlock.Text = string.Format("Player assigned. Starting in {0} second(s)", remainingSeconds);
        }

        private void ServerCompleted()
        {
            // Sprite selection not allowed, server completed, disconnect, and navigate back.
            this.networkService.Disconnect();
            WaveServices.TimerFactory.CreateTimer(TimeSpan.FromSeconds(1), () =>
            {
                WaveServices.ScreenContextManager.Pop(false);
            }, false, this);
        }
    }
}