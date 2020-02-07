﻿using Newtonsoft.Json.Linq;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("SharedDoors", "dbteku", "1.0.0")]
    [Description("Making sharing doors easier.")]
    public class SharedDoors : RustPlugin
    {
        [PluginReference]
        private Plugin Clans;
        [PluginReference]
        private Plugin Friends;

        private static SharedDoors instance;
        private const string RUST_IO = "clans";
        private const string CLANS_NAME = "Clans";
        private const string RUST_CLANS_HOOK = "SharedDoors now hooking to Rust:IO Clans";
        private const string RUST_CLANS_NOT_FOUND = "Rust Clans has not been found.";
        private const string MASTER_PERM = "shareddoors.master";
        private MasterKeyHolders holders;

        private void OnServerInitialized()
        {
            instance = this;
            permission.RegisterPermission(MASTER_PERM, this);
            holders = new MasterKeyHolders();
            if (Clans == null)
            {
                Puts(RUST_CLANS_NOT_FOUND);
            }
            else
            {
                Puts(RUST_CLANS_HOOK);
            }
        }

        private void Unload()
        {
            instance = null;
        }

        private void OnPluginLoaded(Plugin plugin)
        {
            if (plugin.Name == CLANS_NAME)
            {
                Puts(RUST_CLANS_HOOK);
                Clans = plugin;
            }
        }

        private void OnPluginUnloaded(Plugin name)
        {
            if (name.Name == CLANS_NAME)
            {
                Puts(RUST_CLANS_HOOK);
                Clans = null;
            }
        }

        private void OnPlayerInit(BasePlayer player)
        {
            IPlayer iPlayer = covalence.Players.FindPlayerById(player.userID.ToString());
            if (player.IsAdmin || iPlayer.HasPermission(MASTER_PERM))
            {
                holders.AddMaster(player.userID.ToString());
            }
        }

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            IPlayer iPlayer = covalence.Players.FindPlayerById(player.userID.ToString());
            if (player.IsAdmin || iPlayer.HasPermission(MASTER_PERM))
            {
                holders.RemoveMaster(player.userID.ToString());
            }
        }

        private bool CanUseLockedEntity(BasePlayer player, BaseLock door)
        {
            IPlayer iPlayer = covalence.Players.FindPlayerById(player.userID.ToString());
            bool canUse = false;
            canUse = (player.IsAdmin && holders.IsAKeyMaster(player.userID.ToString()))
            || (iPlayer.HasPermission(MASTER_PERM) && holders.IsAKeyMaster(player.userID.ToString()))
            || new DoorAuthorizer(door, player).CanOpen();
            return canUse;
        }
        #region Commands
        [ChatCommand("sd")]
        private void SharedDoorsCommand(BasePlayer basePlayer, string command, string[] args)
        {
            IPlayer player = covalence.Players.FindPlayerById(basePlayer.userID.ToString());
            if (args.Length > 0)
            {
                if (args[0].ToLower() == "help")
                {
                    PlayerResponder.NotifyUser(player, "Master Mode Toggle: /sd masterMode");
                }
                else if (args[0].ToLower() == "mastermode" || args[0].ToLower() == "mm")
                {
                    if (player.IsAdmin || player.HasPermission(MASTER_PERM))
                    {
                        if (holders.HasMaster(player.Id))
                        {
                            holders.ToggleMasterMode(player.Id);
                            if (holders.IsAKeyMaster(player.Id))
                            {
                                PlayerResponder.NotifyUser(player, "Master Mode Enabled. You can now open all doors and chests.");
                            }
                            else
                            {
                                PlayerResponder.NotifyUser(player, "Master Mode Disabled. You can no longer open all doors and chests.");
                            }
                        }
                        else
                        {
                            holders.AddMaster(player.Id);
                            holders.GiveMasterKey(player.Id);
                            PlayerResponder.NotifyUser(player, "Master Mode Enabled. You can now open all doors and chests.");
                        }
                    }
                    else
                    {
                        PlayerResponder.NotifyUser(player, "Master Mode Not Available. You don't have permission to use this command.");
                    }
                }
            }
            else
            {
                PlayerResponder.NotifyUser(player, "Master Mode Toggle: /sd masterMode");
            }
        }
        #endregion

        public static SharedDoors getInstance()
        {
            return instance;
        }

        private class PlayerResponder
        {
            private const string PREFIX = "<color=#00ffffff>[</color><color=#ff0000ff>SharedDoors</color><color=#00ffffff>]</color>";

            public static void NotifyUser(IPlayer player, String message)
            {
                player.Message(message, PREFIX);
            }
        }

        /*
         *
         * Door Handler Class
         *
         * */

        private class DoorAuthorizer
        {
            public BaseLock BaseDoor { get; protected set; }
            public BasePlayer Player { get; protected set; }
            private ToolCupboardChecker checker;
            private RustIOHandler handler;

            public DoorAuthorizer(BaseLock door, BasePlayer player)
            {
                this.BaseDoor = door;
                this.Player = player;
                checker = new ToolCupboardChecker(Player);
                handler = new RustIOHandler(this);
            }

            public bool CanOpen()
            {
                bool canUse = false;
                if (BaseDoor.IsLocked())
                {
                    if (BaseDoor is CodeLock)
                    {
                        CodeLock codeLock = (CodeLock)BaseDoor;
                        canUse = CanOpenCodeLock(codeLock, Player);
                    }
                    else if (BaseDoor is KeyLock)
                    {
                        KeyLock keyLock = (KeyLock)BaseDoor;
                        canUse = CanOpenKeyLock(keyLock, Player);
                    }
                }
                else
                {
                    canUse = true;
                }
                return canUse;
            }

            private bool CanOpenCodeLock(CodeLock door, BasePlayer player)
            {
                bool canUse = false;
                var whitelist = door.whitelistPlayers;
                canUse = whitelist.Contains(player.userID);

                if (!canUse)
                {
                    canUse = (player.CanBuild() && checker.IsPlayerAuthorized());
                    if (canUse && handler.ClansAvailable())
                    {
                        canUse = handler.IsInClan(player.UserIDString, door.OwnerID.ToString()) || handler.IsFriend(player.UserIDString, door.OwnerID.ToString());
                    }
                }

                PlaySound(canUse, door, player);
                return canUse;
            }

            private bool CanOpenKeyLock(KeyLock door, BasePlayer player)
            {
                return door.HasLockPermission(player) 
                || (player.CanBuild() 
                    && checker.IsPlayerAuthorized() 
                    && (handler.IsInClan(player.UserIDString, door.OwnerID.ToString()) || handler.IsFriend(player.UserIDString, door.OwnerID.ToString())));
            }

            private void PlaySound(bool canUse, CodeLock door, BasePlayer player)
            {
                if (canUse)
                {
                    Effect.server.Run(door.effectUnlocked.resourcePath, player.transform.position, Vector3.zero, null, false);
                }
                else
                {
                    Effect.server.Run(door.effectDenied.resourcePath, player.transform.position, Vector3.zero, null, false);
                }
            }
        }

        /*
         *
         * Tool Cupboard Tool
         *
         * */

        private class ToolCupboardChecker
        {
            public BasePlayer Player { get; protected set; }

            public ToolCupboardChecker(BasePlayer player)
            {
                this.Player = player;
            }

            public bool IsPlayerAuthorized()
            {
                return Player.IsBuildingAuthed();
            }
        }

        /*
         *
         * RustIO Handler
         *
         * */

        private class RustIOHandler
        {
            private const string GET_CLAN_OF_PLAYER = "GetClanOf";
            private const string IS_CLAN_MEMBER = "IsClanMember";
            public Plugin Clans { get; protected set; }
            public Plugin Friends { get; protected set; }
            public ulong OriginalPlayerID { get; protected set; }
            public DoorAuthorizer Door { get; protected set; }

            public RustIOHandler(DoorAuthorizer door)
            {
                if (door.BaseDoor is CodeLock)
                {
                    CodeLock codeLock = door.BaseDoor as CodeLock;
                    List<ulong> whitelist = codeLock.whitelistPlayers;
                    if (whitelist.Count > 0)
                    {
                        this.OriginalPlayerID = whitelist[0];
                    }
                    else
                    {
                        this.OriginalPlayerID = 0;
                    }
                }
                this.Door = door;
                this.Clans = SharedDoors.getInstance().Clans;
                this.Friends = SharedDoors.getInstance().Friends;
            }

            public bool IsInClan(string playerId, string playerDoorOwner)
            {
                bool isInClan = false;
                if (ClansAvailable())
                {
                    string clanName = Clans.Call<string>(GET_CLAN_OF_PLAYER, playerId);
                    if (!string.IsNullOrWhiteSpace(clanName))
                    {
                        isInClan = Clans.Call<bool>(IS_CLAN_MEMBER, playerDoorOwner, playerId);
                    }
                }

                return isInClan;
            }

            public bool IsFriend(string playerId, string playerDoorOwner)
            {
                return Friends.IsLoaded && Friends.Call<bool>("IsFriend", playerId, playerDoorOwner);
            }

            public bool ClansAvailable()
            {
                return this.Clans != null && Clans.IsLoaded;
            }
        }

        /*
       *
       * Admin Mode Handler
       *
       * */

        private class MasterKeyHolders
        {
            private Dictionary<string, PlayerSettings> keyMasters;

            public MasterKeyHolders()
            {
                keyMasters = new Dictionary<string, PlayerSettings>();
            }

            public void AddMaster(String id)
            {
                this.keyMasters.Add(id, new PlayerSettings(false));
            }

            public void RemoveMaster(String id)
            {
                this.keyMasters.Remove(id);
            }

            public void GiveMasterKey(String id)
            {
                PlayerSettings settings = null;
                bool exists = keyMasters.TryGetValue(id, out settings);
                if (exists)
                {
                    settings.IsMasterKeyHolder = true;
                }
            }

            public void RemoveMasterKey(String id)
            {
                PlayerSettings settings = null;
                bool exists = keyMasters.TryGetValue(id, out settings);
                if (exists)
                {
                    settings.IsMasterKeyHolder = false;
                }
            }

            public bool IsAKeyMaster(String id)
            {
                bool isKeyMaster = false;
                PlayerSettings settings = null;
                bool exists = keyMasters.TryGetValue(id, out settings);
                if (exists)
                {
                    isKeyMaster = settings.IsMasterKeyHolder;
                }
                return isKeyMaster;
            }

            public void ToggleMasterMode(String id)
            {
                PlayerSettings settings = null;
                bool exists = keyMasters.TryGetValue(id, out settings);
                if (exists)
                {
                    settings.ToggleMasterMode();
                }
            }

            public bool HasMaster(string id)
            {
                return keyMasters.ContainsKey(id);
            }
        }

        /*
       *
       * Player Settings
       *
       * */

        private class PlayerSettings
        {
            public bool IsMasterKeyHolder { get; set; }

            public PlayerSettings(bool isMasterKeyHolder)
            {
                IsMasterKeyHolder = isMasterKeyHolder;
            }

            public void ToggleMasterMode()
            {
                IsMasterKeyHolder = !IsMasterKeyHolder;
            }
        }
    }
}