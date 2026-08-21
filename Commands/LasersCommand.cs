using System;
using System.Linq;
using CommandSystem;
using EyeLasers.Controllers;
using RemoteAdmin;

namespace EyeLasers.Commands
{
    [CommandHandler(typeof(ClientCommandHandler))]
    [CommandHandler(typeof(RemoteAdminCommandHandler))]
    public class LasersCommand : ICommand
    {
        public string Command => "lasers";
        public string[] Aliases => new[] { "laser", "eyelasers" };
        public string Description => "Управление лазерами: .lasers on [sec], .lasers off, .lasers hide, .lasers give <id> [sec]";

        public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
        {
            ReferenceHub senderHub = null;

            if (sender is PlayerCommandSender pcs)
            {
                senderHub = pcs.ReferenceHub;
            }

            if (!EyeLasersPlugin.HasPermission(senderHub))
            {
                response = "У вас нет прав на использование этой команды.";
                return false;
            }

            if (arguments.Count == 0)
            {
                response = "Команды:\n.lasers on [сек] - Включить лазеры себе\n.lasers off - Выключить лазеры себе\n.lasers hide - Скрыть/показать модель лучей локально (оставить свет)\n.lasers give <id/all> [сек] - Выдать лазеры игроку";
                return false;
            }

            string subCommand = arguments.At(0).ToLower();

            switch (subCommand)
            {
                case "on":
                    if (senderHub == null)
                    {
                        response = "Команда доступна только игроку.";
                        return false;
                    }

                    if (!EyeLasersPlugin.IsAlive(senderHub))
                    {
                        response = "Вы должны быть живы.";
                        return false;
                    }

                    if (!EyeLasersPlugin.TryGetRoleOffsets(senderHub.roleManager.CurrentRole.RoleTypeId, out _))
                    {
                        response = "Ваша текущая роль не поддерживает лазеры из глаз.";
                        return false;
                    }

                    float durationOn = -1f;
                    if (arguments.Count > 1 && float.TryParse(arguments.At(1), out float secOn))
                    {
                        durationOn = secOn;
                    }

                    EyeLasersPlugin.Instance.AddLaser(senderHub, durationOn);
                    response = durationOn > 0 ? $"Запуск лазерного взгляда на {durationOn} сек..." : "Запуск лазерного взгляда...";
                    return true;

                case "off":
                    if (senderHub == null)
                    {
                        response = "Команда доступна только игроку.";
                        return false;
                    }

                    EyeLasersPlugin.Instance.RemoveLaser(senderHub);
                    response = "Отключение лазерного взгляда...";
                    return true;

                case "hide":
                case "stealth":
                    if (senderHub == null)
                    {
                        response = "Команда доступна только игроку.";
                        return false;
                    }

                    EyeLasersPlugin.Instance.HandleToggleHide(senderHub);
                    response = "Переключение локальной видимости моделей лучей.";
                    return true;

                case "give":
                    if (arguments.Count < 2)
                    {
                        response = "Использование: .lasers give <ID игрока / all> [длительность_сек]";
                        return false;
                    }

                    string targetArg = arguments.At(1).ToLower();
                    float durationGive = -1f;
                    if (arguments.Count > 2 && float.TryParse(arguments.At(2), out float parsedDur))
                    {
                        durationGive = parsedDur;
                    }

                    if (targetArg == "all")
                    {
                        int count = 0;
                        foreach (ReferenceHub hub in ReferenceHub.AllHubs)
                        {
                            if (EyeLasersPlugin.IsAlive(hub) && EyeLasersPlugin.TryGetRoleOffsets(hub.roleManager.CurrentRole.RoleTypeId, out _))
                            {
                                EyeLasersPlugin.Instance.AddLaser(hub, durationGive);
                                count++;
                            }
                        }

                        response = $"Лазеры запущены для {count} чел." + (durationGive > 0 ? $" на {durationGive} сек." : "");
                        return true;
                    }

                    if (int.TryParse(targetArg, out int targetId))
                    {
                        ReferenceHub targetHub = ReferenceHub.AllHubs.FirstOrDefault(h => h.PlayerId == targetId);
                        if (targetHub == null)
                        {
                            response = $"Игрок с ID {targetId} не найден.";
                            return false;
                        }

                        if (!EyeLasersPlugin.IsAlive(targetHub))
                        {
                            response = $"Игрок {targetHub.nicknameSync.MyNick} мертв.";
                            return false;
                        }

                        if (!EyeLasersPlugin.TryGetRoleOffsets(targetHub.roleManager.CurrentRole.RoleTypeId, out _))
                        {
                            response = $"Роль игрока {targetHub.nicknameSync.MyNick} не поддерживает лазеры.";
                            return false;
                        }

                        if (EyeLasersPlugin.Instance.ActiveLasers.ContainsKey(targetHub))
                        {
                            EyeLasersPlugin.Instance.RemoveLaser(targetHub);
                            response = $"Лазеры отключаются для {targetHub.nicknameSync.MyNick}.";
                        }
                        else
                        {
                            EyeLasersPlugin.Instance.AddLaser(targetHub, durationGive);
                            response = $"Лазеры активируются для {targetHub.nicknameSync.MyNick}" + (durationGive > 0 ? $" на {durationGive} сек." : ".");
                        }

                        return true;
                    }

                    response = "Некорректный ID игрока.";
                    return false;

                default:
                    response = "Неизвестная команда. Доступно: on [sec], off, hide, give <id/all> [sec]";
                    return false;
            }
        }
    }
}