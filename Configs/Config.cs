using System.Collections.Generic;
using System.ComponentModel;

namespace EyeLasers.Configs
{
    public class EyeLasersConfig
    {
        [Description("Включить ограничение доступа (по UserID / группам)")]
        public bool RestrictAccess { get; set; } = false;

        [Description("Список UserID игроков с доступом")]
        public List<string> AllowedUserIds { get; set; } = new List<string> { "76561198000000000@steam" };

        [Description("Список групп RemoteAdmin с доступом")]
        public List<string> AllowedGroups { get; set; } = new List<string> { "owner", "admin", "donor", "vip" };

        [Description("Включить нанесение урона при стрельбе")]
        public bool EnableDamage { get; set; } = true;

        [Description("Количество урона за тик (люди)")]
        public float DamagePerTickHuman { get; set; } = 15f;

        [Description("Количество урона за тик (SCP)")]
        public float DamagePerTickScp { get; set; } = 25f;

        [Description("Интервал между тиками урона в секундах")]
        public float DamageInterval { get; set; } = 3.0f;

        [Description("Причина смерти")]
        public string DeathReason { get; set; } = "Испепелен лазерным взглядом";

        [Description("Логировать события в консоль")]
        public bool LogToConsole { get; set; } = true;

        [Description("Discord Webhook URL")]
        public string DiscordWebhookUrl { get; set; } = "";

        [Description("Общий переключатель детонации взрывчатки")]
        public bool DetonateExplosives { get; set; } = true;

        [Description("Взрывать гранаты в руках у противника")]
        public bool DetonateInHands { get; set; } = true;

        [Description("Взрывать летящие снаряды в воздухе")]
        public bool DetonateFlyingProjectiles { get; set; } = true;

        [Description("Взрывать лежащие на полу гранаты")]
        public bool DetonateFloorPickups { get; set; } = true;

        [Description("Взрывать лежащие на полу коробки с патронами")]
        public bool DetonateAmmo { get; set; } = true;

        [Description("Радиус захвата взрывчатки вокруг луча (в метрах)")]
        public float DetonationRadius { get; set; } = 1.5f;

        [Description("Длительность анимации запуска")]
        public float StartupDuration { get; set; } = 0.25f;

        [Description("Длительность анимации затухания")]
        public float ShutdownDuration { get; set; } = 0.20f;

        [Description("Включить разрушение дверей")]
        public bool DamageDoors { get; set; } = true;

        [Description("Включить разрушение стекол")]
        public bool BreakWindows { get; set; } = true;

        [Description("Включить следы выжигания на поверхностях")]
        public bool EnableScorchMarks { get; set; } = true;

        [Description("Максимальное число следов выжигания в пуле")]
        public int ScorchPoolSize { get; set; } = 20;

        [Description("Включить динамическое освещение")]
        public bool EnableLights { get; set; } = true;

        [Description("Интенсивность света в точке попадания")]
        public float ImpactLightIntensity { get; set; } = 7.0f;

        [Description("Радиус света в точке попадания")]
        public float ImpactLightRange { get; set; } = 10f;

        [Description("Интенсивность света у глаз")]
        public float EyeLightIntensity { get; set; } = 3.5f;

        [Description("Включить меню в настройках клиента (Server-Specific Settings)")]
        public bool EnableKeybindSetting { get; set; } = true;

        [Description("ID бинда включения лазеров")]
        public int KeybindSettingId { get; set; } = 1050;

        [Description("Текст бинда активации")]
        public string KeybindLabel { get; set; } = "Лазерный взгляд (Вкл/Выкл)";

        [Description("ID переключателя скрытия модели")]
        public int HideModelToggleSettingId { get; set; } = 1051;

        [Description("Текст переключателя скрытия модели")]
        public string HideModelToggleLabel { get; set; } = "Скрыть модель лучей (Только свет)";
    }
}