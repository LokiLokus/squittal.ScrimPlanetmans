﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using DaybreakGames.Census;
using DaybreakGames.Census.JsonConverters;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using squittal.LivePlanetmans.CensusStream;
using squittal.ScrimPlanetmans.CensusStream.Models;
using squittal.ScrimPlanetmans.ScrimMatch;
using squittal.ScrimPlanetmans.ScrimMatch.Events;
using squittal.ScrimPlanetmans.ScrimMatch.Models;
using squittal.ScrimPlanetmans.Services.Planetside;
using squittal.ScrimPlanetmans.Services.ScrimMatch;
using squittal.ScrimPlanetmans.Shared.Models;
using squittal.ScrimPlanetmans.Shared.Models.Planetside;
using squittal.ScrimPlanetmans.Shared.Models.Planetside.Events;

//using Microsoft.EntityFrameworkCore;
//using squittal.ScrimPlanetmans.Data;

namespace squittal.ScrimPlanetmans.CensusStream
{
    public class WebsocketEventHandler : IWebsocketEventHandler
    {
        //private readonly IDbContextHelper _dbContextHelper;
        private readonly IItemService _itemService;
        private readonly ICharacterService _characterService;
        private readonly IScrimTeamsManager _teamsManager;
        private readonly IScrimMatchScorer _scorer;
        private readonly IScrimMessageBroadcastService _messageService;
        private readonly ILogger<WebsocketEventHandler> _logger;
        private readonly Dictionary<string, MethodInfo> _processMethods;

        private bool _isScoringEnabled = false;

        // Credit to Voidwell @Lampjaw
        private readonly JsonSerializer _payloadDeserializer = JsonSerializer.Create(new JsonSerializerSettings
        {
            ContractResolver = new UnderscorePropertyNamesContractResolver(),
            Converters = new JsonConverter[]
                {
                    new BooleanJsonConverter(),
                    new DateTimeJsonConverter()
                }
        });

        public WebsocketEventHandler(IScrimTeamsManager teamsManager, ICharacterService characterService, IScrimMatchScorer scorer, IItemService itemService, IScrimMessageBroadcastService messageService, ILogger<WebsocketEventHandler> logger)
        {
            _teamsManager = teamsManager;
            _itemService = itemService;
            _messageService = messageService;
            //_dbContextHelper = dbContextHelper;
            _characterService = characterService;
            _scorer = scorer;
            _logger = logger;

            // Credit to Voidwell @ Lampjaw
            _processMethods = GetType()
                .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
                .Where(m => m.GetCustomAttribute<CensusEventHandlerAttribute>() != null)
                .ToDictionary(m => m.GetCustomAttribute<CensusEventHandlerAttribute>().EventName);
        }

        public void EnabledScoring()
        {
            _isScoringEnabled = true;
        }

        public void DisableScoring()
        {
            _isScoringEnabled = false;
        }

        public async Task Process(JToken message)
        {
            await ProcessServiceEvent(message);
        }

        // Credit to Voidwell @Lampjaw
        private async Task ProcessServiceEvent(JToken message)
        {
            var jPayload = message.SelectToken("payload");

            var payload = jPayload?.ToObject<PayloadBase>(_payloadDeserializer);
            var eventName = payload?.EventName;

            if (eventName == null)
            {
                return;
            }

            _logger.LogDebug("Payload received for event: {0}.", eventName);

            //var eventName1 = jPayload.Value<string>("event_name");

            //if (eventName == "PlayerLogin" || eventName == "PlayerLogout")
            //{
            //    _logger.LogInformation($"Payload received for event {eventName}: {payload.ToString()}");
            //}

            //if (eventName1 == "PlayerLogin" || eventName1 == "PlayerLogout")
            //{
            //    _logger.LogInformation($"Payload received for event1 {eventName}: {payload.ToString()}");
            //}

            if (!_processMethods.ContainsKey(eventName))
            {
                _logger.LogWarning("No process method found for event: {0}", eventName);
                return;
            }

            if (payload.ZoneId.HasValue && payload.ZoneId.Value > 1000)
            {
                return;
            }

            try
            {
                //var inputType = _processMethods[eventName].GetCustomAttribute<CensusEventHandlerAttribute>().PayloadType;
                //var inputParam = jPayload.ToObject(inputType, _payloadDeserializer);

                //await (Task)_processMethods[eventName].Invoke(this, new[] { inputParam });

                switch (eventName)
                {
                    case "Death":
                        var deathParam = jPayload.ToObject<DeathPayload>(_payloadDeserializer);
                        await Process(deathParam);
                        break;

                    case "PlayerLogin":
                        var loginParam = jPayload.ToObject<PlayerLoginPayload>(_payloadDeserializer);
                        await Task.Run(()=>
                        { 
                            Process(loginParam);
                        });
                        break;

                    case "PlayerLogout":
                        var logoutParam = jPayload.ToObject<PlayerLogoutPayload>(_payloadDeserializer);
                        await Task.Run(() =>
                        {
                            Process(logoutParam);
                        });
                        break;

                    case "GainExperience":
                        var experienceParam = jPayload.ToObject<GainExperiencePayload>(_payloadDeserializer);
                        await Task.Run(() =>
                        {
                            Process(experienceParam);
                        });
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(75642, ex, "Failed to process websocket event: {0}.", eventName);
            }
        }

        #region Payload Handling

        [CensusEventHandler("Death", typeof(DeathPayload))]
        private async Task<ScrimDeathActionEvent> Process(DeathPayload payload)
        {
            string attackerId = payload.AttackerCharacterId;
            string victimId = payload.CharacterId;

            bool isValidAttackerId = (attackerId != null && attackerId.Length > 18);
            bool isValidVictimId = (victimId != null && victimId.Length > 18);

            Player attackerPlayer;
            Player victimPlayer;

            ScrimDeathActionEvent deathEvent = new ScrimDeathActionEvent
            {
                Timestamp = payload.Timestamp,
                ZoneId = payload.ZoneId,
                IsHeadshot = payload.IsHeadshot
            };

            var weaponItem = await _itemService.GetItem((int)payload.AttackerWeaponId);
            if (weaponItem != null)
            {
                deathEvent.Weapon = new ScrimActionWeaponInfo()
                {
                    Id = weaponItem.Id,
                    ItemCategoryId = (int)weaponItem.ItemCategoryId,
                    Name = weaponItem.Name,
                    IsVehicleWeapon = weaponItem.IsVehicleWeapon
                };
            }
            

            try
            {
                if (isValidAttackerId == true)
                {
                    deathEvent.AttackerCharacterId = attackerId;
                    deathEvent.AttackerLoadoutId = payload.AttackerLoadoutId;
                    
                    attackerPlayer = _teamsManager.GetPlayerFromId(attackerId);
                    deathEvent.AttackerPlayer = attackerPlayer;

                    if (attackerPlayer != null)
                    {
                        _teamsManager.SetPlayerLoadoutId(attackerId, deathEvent.AttackerLoadoutId);

                    }
                }

                if (isValidVictimId == true)
                {
                    deathEvent.VictimCharacterId = victimId;
                    deathEvent.VictimLoadoutId = payload.CharacterLoadoutId;

                    victimPlayer = _teamsManager.GetPlayerFromId(victimId);
                    deathEvent.VictimPlayer = victimPlayer;

                    if (victimPlayer != null)
                    {
                        _teamsManager.SetPlayerLoadoutId(victimId, deathEvent.VictimLoadoutId);
                    }
                }

                deathEvent.ActionType = GetDeathScrimActionType(deathEvent);

                if (deathEvent.ActionType != ScrimActionType.OutsideInterference)
                {
                    deathEvent.DeathType = GetDeathEventType(deathEvent.ActionType);

                    if (deathEvent.DeathType == DeathEventType.Suicide)
                    {
                        deathEvent.AttackerPlayer = deathEvent.VictimPlayer;
                        deathEvent.AttackerCharacterId = deathEvent.VictimCharacterId;
                        deathEvent.AttackerLoadoutId = deathEvent.VictimLoadoutId;
                    }

                    if (_isScoringEnabled)
                    {
                        //_scorer.ScoreDeathEvent(dataModel);
                        var points = _scorer.ScoreDeathEvent(deathEvent);
                        deathEvent.Points = points;
                    }
                }

                //var dataModel = new Death
                //{
                //    AttackerCharacterId = attackerId,
                //    AttackerFireModeId = payload.AttackerFireModeId,
                //    AttackerLoadoutId = payload.AttackerLoadoutId,
                //    AttackerVehicleId = payload.AttackerVehicleId,
                //    AttackerWeaponId = payload.AttackerWeaponId,
                //    //AttackerOutfitId = attackerOutfitTask?.Result?.OutfitId,
                //    //AttackerTeamOrdinal = attackerTeamOrdinal,
                //    AttackerFactionId = attackerFactionId,
                //    CharacterId = victimId,
                //    CharacterLoadoutId = payload.CharacterLoadoutId,
                //    //CharacterOutfitId = victimOutfitTask?.Result?.OutfitId,
                //    //CharacterTeamOrdinal = victimTeamOrdinal,
                //    CharacterFactionId = victimFactionId,
                //    IsHeadshot = payload.IsHeadshot,
                //    DeathEventType = deathEventType,
                //    Timestamp = payload.Timestamp,
                //    WorldId = payload.WorldId,
                //    ZoneId = payload.ZoneId.Value
                //};

                //if (_isScoringEnabled)
                //{
                //    //_scorer.ScoreDeathEvent(dataModel);
                //    var points = _scorer.ScoreDeathEvent(deathEvent);
                //    deathEvent.Points = points;
                //}

                _messageService.BroadcastPlayerScrimDeathEventMessage(new ScrimDeathActionEventMessage(deathEvent));

                //return dataModel;
                return deathEvent;

                //dbContext.Deaths.Add(dataModel);
                //await dbContext.SaveChangesAsync();
            }
            catch (Exception)
            {
                //Ignore
                return null;
            }
        }

        private ScrimActionType GetDeathScrimActionType(ScrimDeathActionEvent death)
        {
            // Determine if this is involves a non-tracked player
            if ((death.AttackerPlayer == null && !string.IsNullOrWhiteSpace(death.AttackerCharacterId))
                    || (death.VictimPlayer == null && !string.IsNullOrWhiteSpace(death.VictimCharacterId)))
            {
                return ScrimActionType.OutsideInterference;
            }

            var attackerIsVehicle = death.Weapon.IsVehicleWeapon;

            var attackerIsMax = death.AttackerLoadoutId == null
                                    ? false
                                    : ProfileService.IsMaxLoadoutId(death.AttackerLoadoutId);

            var victimIsMax = death.VictimLoadoutId == null
                                    ? false
                                    : ProfileService.IsMaxLoadoutId(death.VictimLoadoutId);

            var sameTeam = _teamsManager.DoPlayersShareTeam(death.AttackerPlayer, death.VictimPlayer);
            var samePlayer = (death.AttackerPlayer == death.VictimPlayer || death.AttackerPlayer == null);

            if (samePlayer)
            {
                return victimIsMax
                            ? ScrimActionType.MaxSuicide
                            : ScrimActionType.InfantrySuicide;
            }
            else if (sameTeam)
            {
                if (attackerIsVehicle)
                {
                    return victimIsMax
                                ? ScrimActionType.VehicleTeamkillMax
                                : ScrimActionType.VehicleTeamkillInfantry;
                }
                else if (attackerIsMax)
                {
                    return victimIsMax
                                ? ScrimActionType.MaxTeamkillMax
                                : ScrimActionType.MaxTeamkillInfantry;
                }
                else
                {
                    return victimIsMax
                                ? ScrimActionType.InfantryTeamkillMax
                                : ScrimActionType.InfantryTeamkillInfantry;
                }
            }
            else
            {
                if (attackerIsVehicle)
                {
                    return victimIsMax
                                ? ScrimActionType.VehicleKillMax
                                : ScrimActionType.VehicleKillInfantry;
                }
                else if (attackerIsMax)
                {
                    return victimIsMax
                                ? ScrimActionType.MaxKillMax
                                : ScrimActionType.MaxKillInfantry;
                }
                else
                {
                    return victimIsMax
                                ? ScrimActionType.InfantryKillMax
                                : ScrimActionType.InfantryKillInfantry;
                }
            }
        }

        private DeathEventType GetDeathEventType(ScrimActionType scrimActionType)
        {
            return scrimActionType switch
            {
                ScrimActionType.MaxSuicide => DeathEventType.Suicide,
                ScrimActionType.InfantrySuicide => DeathEventType.Suicide,
                ScrimActionType.MaxTeamkillMax => DeathEventType.Teamkill,
                ScrimActionType.MaxTeamkillInfantry => DeathEventType.Teamkill,
                ScrimActionType.InfantryTeamkillMax => DeathEventType.Teamkill,
                ScrimActionType.InfantryTeamkillInfantry => DeathEventType.Teamkill,
                ScrimActionType.VehicleTeamkillMax => DeathEventType.Teamkill,
                ScrimActionType.VehicleTeamkillInfantry => DeathEventType.Teamkill,
                ScrimActionType.MaxKillMax => DeathEventType.Kill,
                ScrimActionType.MaxKillInfantry => DeathEventType.Kill,
                ScrimActionType.InfantryKillMax => DeathEventType.Kill,
                ScrimActionType.InfantryKillInfantry => DeathEventType.Kill,
                ScrimActionType.VehicleKillMax => DeathEventType.Kill,
                ScrimActionType.VehicleKillInfantry => DeathEventType.Kill,
                _ => DeathEventType.Kill
            };
        }

        #region Login / Logout Payloads
        [CensusEventHandler("PlayerLogin", typeof(PlayerLoginPayload))]
        //private Task<PlayerLogin> Process(PlayerLoginPayload payload)
        private PlayerLogin Process(PlayerLoginPayload payload)
        {
            var characterId = payload.CharacterId;

            var player = _teamsManager.GetPlayerFromId(characterId);
            
            // TODO: use ScrimActionLoginEvent instead of PlayerLogin

            var dataModel = new PlayerLogin
            {
                CharacterId = payload.CharacterId,
                Timestamp = payload.Timestamp,
                WorldId = payload.WorldId
            };

            _scorer.HandlePlayerLogin(dataModel);

            _messageService.BroadcastPlayerLoginMessage(new PlayerLoginMessage(player, dataModel));

            return dataModel;
        }

        [CensusEventHandler("PlayerLogout", typeof(PlayerLogoutPayload))]
        //private Task<PlayerLogout> Process(PlayerLogoutPayload payload)
        private PlayerLogout Process(PlayerLogoutPayload payload)
        {
            var characterId = payload.CharacterId;

            var player = _teamsManager.GetPlayerFromId(characterId);

            // TODO: use ScrimActionLogoutEvent instead of PlayerLogout

            var dataModel = new PlayerLogout
            {
                CharacterId = payload.CharacterId,
                Timestamp = payload.Timestamp,
                WorldId = payload.WorldId
            };

            _scorer.HandlePlayerLogout(dataModel);

            _messageService.BroadcastPlayerLogoutMessage(new PlayerLogoutMessage(player, dataModel));

            return dataModel;
        }
        #endregion

        #region GainExperience Payloads
        //private Task<GainExperience> Process(GainExperiencePayload payload)
        [CensusEventHandler("GainExperience", typeof(GainExperiencePayload))]
        private void Process(GainExperiencePayload payload)
        {
            var experienceId = payload.ExperienceId;
            var experienceType = ExperienceEventsBuilder.GetExperienceTypeFromId(experienceId);

            var baseEvent = new ScrimExperienceGainActionEvent
            {
                Timestamp = payload.Timestamp,
                ZoneId = payload.ZoneId,

                ExperienceType = experienceType,
                ExperienceGainInfo = new ScrimActionExperienceGainInfo
                {
                    Id = experienceId,
                    Amount = payload.Amount
                },
                LoadoutId = payload.LoadoutId
            };

            switch (experienceType)
            {
                case ExperienceType.Revive:
                    ProcessRevivePayload(baseEvent, payload);
                    return;

                case ExperienceType.DamageAssist:
                    ProcessAssistPayload(baseEvent, payload);
                    return;

                case ExperienceType.UtilityAssist:
                    ProcessAssistPayload(baseEvent, payload);
                    return;

                case ExperienceType.PointControl:
                    ProcessPointControlPayload(baseEvent, payload);
                    return;

                default:
                    return;
            }

            //var characterId = payload.CharacterId;

            //var player = _teamsManager.GetPlayerFromId(characterId);


            //var dataModel = new GainExperience
            //{
            //    Id = Guid.NewGuid(),
            //    ExperienceId = payload.ExperienceId,
            //    CharacterId = payload.CharacterId,
            //    Amount = payload.Amount,
            //    LoadoutId = payload.LoadoutId,
            //    OtherId = payload.OtherId,
            //    Timestamp = payload.Timestamp,
            //    WorldId = payload.WorldId,
            //    ZoneId = payload.ZoneId.Value
            //};

            //return Task.FromResult(dataModel);
            //return dataModel;
        }

        private void ProcessRevivePayload(ScrimExperienceGainActionEvent baseEvent, GainExperiencePayload payload)
        {
            var reviveEvent = new ScrimReviveActionEvent(baseEvent);

            string medicId = payload.CharacterId;
            string revivedId = payload.OtherId;

            bool isValidMedicId = (medicId != null && medicId.Length > 18);
            bool isValidRevivedId = (revivedId != null && revivedId.Length > 18);

            Player medicPlayer;
            Player revivedPlayer;

            if (isValidMedicId == true)
            {
                reviveEvent.MedicCharacterId = medicId;

                medicPlayer = _teamsManager.GetPlayerFromId(medicId);
                reviveEvent.MedicPlayer = medicPlayer;

                _teamsManager.SetPlayerLoadoutId(medicId, reviveEvent.LoadoutId);
            }

            if (isValidRevivedId == true)
            {
                reviveEvent.RevivedCharacterId = revivedId;

                revivedPlayer = _teamsManager.GetPlayerFromId(revivedId);
                reviveEvent.RevivedPlayer = revivedPlayer;
            }

            reviveEvent.ActionType = GetReviveScrimActionType(reviveEvent);

            if (reviveEvent.ActionType != ScrimActionType.OutsideInterference)
            {
                if (_isScoringEnabled)
                {
                    var points = _scorer.ScoreReviveEvent(reviveEvent);
                    reviveEvent.Points = points;
                }
            }

            // TODO: broadcast Player Revive Event Message
        }

        private ScrimActionType GetReviveScrimActionType(ScrimReviveActionEvent reviveEvent)
        {
            // Determine if this is involves a non-tracked player
            if ((reviveEvent.MedicPlayer == null && !string.IsNullOrWhiteSpace(reviveEvent.MedicCharacterId))
                    || (reviveEvent.RevivedPlayer == null && !string.IsNullOrWhiteSpace(reviveEvent.RevivedCharacterId)))
            {
                return ScrimActionType.OutsideInterference;
            }

            bool isRevivedMax = ProfileService.IsMaxLoadoutId(reviveEvent.RevivedPlayer.LoadoutId);

            return isRevivedMax
                        ? ScrimActionType.ReviveMax
                        : ScrimActionType.ReviveInfantry;
        }

        private void ProcessAssistPayload(ScrimExperienceGainActionEvent baseEvent, GainExperiencePayload payload)
        {
            var assistEvent = new ScrimAssistActionEvent(baseEvent);

            string attackerId = payload.CharacterId;
            string victimId = payload.OtherId;

            bool isValidattackerId = (attackerId != null && attackerId.Length > 18);
            bool isValidvictimId = (victimId != null && victimId.Length > 18);

            Player attackerPlayer;
            Player victimPlayer;

            if (isValidattackerId == true)
            {
                assistEvent.AttackerCharacterId = attackerId;

                attackerPlayer = _teamsManager.GetPlayerFromId(attackerId);
                assistEvent.AttackerPlayer = attackerPlayer;

                _teamsManager.SetPlayerLoadoutId(attackerId, assistEvent.LoadoutId);
            }

            if (isValidvictimId == true)
            {
                assistEvent.VictimCharacterId = victimId;

                victimPlayer = _teamsManager.GetPlayerFromId(victimId);
                assistEvent.VictimPlayer = victimPlayer;
            }

            assistEvent.ActionType = GetAssistScrimActionType(assistEvent);

            if (assistEvent.ActionType != ScrimActionType.OutsideInterference)
            {
                if (_isScoringEnabled)
                {
                    var points = _scorer.ScoreAssistEvent(assistEvent);
                    assistEvent.Points = points;
                }
            }

        }

        private ScrimActionType GetAssistScrimActionType(ScrimAssistActionEvent assistEvent)
        {
            // Determine if this is involves a non-tracked player
            if ((assistEvent.AttackerPlayer == null && !string.IsNullOrWhiteSpace(assistEvent.AttackerCharacterId))
                    || (assistEvent.VictimPlayer == null && !string.IsNullOrWhiteSpace(assistEvent.VictimCharacterId)))
            {
                return ScrimActionType.OutsideInterference;
            }

            return assistEvent.ExperienceType == ExperienceType.DamageAssist
                        ? ScrimActionType.DamageAssist
                        : ScrimActionType.UtilityAssist;
        }

        private void ProcessPointControlPayload(ScrimExperienceGainActionEvent baseEvent, GainExperiencePayload payload)
        {
            var controlEvent = new ScrimObjectivePlayActionEvent(baseEvent);

            string playerId = payload.CharacterId;

            bool isValidAttackerId = (playerId != null && playerId.Length > 18);

            if (!isValidAttackerId)
            {
                return;
            }

            controlEvent.PlayerCharacterId = playerId;

            var player = _teamsManager.GetPlayerFromId(playerId);
            controlEvent.Player = player;

            _teamsManager.SetPlayerLoadoutId(playerId, controlEvent.LoadoutId);

            controlEvent.ActionType = GetPointControlScrimActionType(controlEvent);

            if (controlEvent.ActionType != ScrimActionType.Unknown)
            {
                if (_isScoringEnabled)
                {
                    var points = _scorer.ScoreObjectivePlayEvent(controlEvent);
                    controlEvent.Points = points;
                }
            }


        }

        private ScrimActionType GetPointControlScrimActionType(ScrimObjectivePlayActionEvent controlEvent)
        {
            var experienceId = controlEvent.ExperienceGainInfo.Id;

            return experienceId switch
            {
                15 => ScrimActionType.PointControl,             // Control Point - Defend (100xp)
                16 => ScrimActionType.PointDefend,              // Control Point - Attack (100xp)
                272 => ScrimActionType.ConvertCapturePoint,     // Convert Capture Point (25xp)
                556 => ScrimActionType.ObjectiveDefensePulse,   // Objective Pulse Defend (50xp)
                557 => ScrimActionType.ObjectiveCapturePulse,   // Objective Pulse Capture (100xp)
                _ => ScrimActionType.Unknown
            };
        }
        #endregion

        [CensusEventHandler("FacilityControl", typeof(FacilityControlPayload))]
        private Task<FacilityControl> Process(FacilityControlPayload payload)
        {
            var dataModel = new FacilityControl
            {
                FacilityId = payload.FacilityId,
                NewFactionId = payload.NewFactionId,
                OldFactionId = payload.OldFactionId,
                DurationHeld = payload.DurationHeld,
                OutfitId = payload.OutfitId,
                Timestamp = payload.Timestamp,
                WorldId = payload.WorldId,
                ZoneId = payload.ZoneId.Value,
            };

            return Task.FromResult(dataModel);
        }

        [CensusEventHandler("PlayerFacilityCapture", typeof(PlayerFacilityCapturePayload))]
        private Task<PlayerFacilityCapture> Process(PlayerFacilityCapturePayload payload)
        {
            var dataModel = new PlayerFacilityCapture
            {
                CharacterId = payload.CharacterId,
                FacilityId = payload.FacilityId,
                OutfitId = payload.OutfitId == "0" ? null : payload.OutfitId,
                Timestamp = payload.Timestamp,
                WorldId = payload.WorldId,
                ZoneId = payload.ZoneId.Value
            };

            return Task.FromResult(dataModel);
        }

        [CensusEventHandler("PlayerFacilityDefend", typeof(PlayerFacilityDefendPayload))]
        private Task<PlayerFacilityDefend> Process(PlayerFacilityDefendPayload payload)
        {
            var dataModel = new PlayerFacilityDefend
            {
                CharacterId = payload.CharacterId,
                FacilityId = payload.FacilityId,
                OutfitId = payload.OutfitId == "0" ? null : payload.OutfitId,
                Timestamp = payload.Timestamp,
                WorldId = payload.WorldId,
                ZoneId = payload.ZoneId.Value
            };

            return Task.FromResult(dataModel);
        }
        #endregion

        public void Dispose()
        {
            return;
        }
    }
}
