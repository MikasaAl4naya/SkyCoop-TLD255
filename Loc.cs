using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;
using Il2Cpp;
using UnityEngine;

namespace SkyCoop
{
    // Russian translation of the mod's own text, used when the game language is Russian.
    //
    // The mod has hundreds of hard-coded English strings, so instead of editing every call site the
    // text is translated where it reaches the screen: HUD messages, confirmation popups, menu titles
    // and rows, chat system lines and the UI prefabs from the asset bundle. Anything without an entry
    // is shown unchanged, so game localization ids and player names pass straight through.
    internal static class Loc
    {
        private static bool Russian = false;
        private static float NextLanguageCheck = -1f;

        public static bool IsRussian
        {
            get
            {
                float Now = Time.realtimeSinceStartup;
                if (Now >= NextLanguageCheck)
                {
                    NextLanguageCheck = Now + 2f;
                    try
                    {
                        string Lang = Localization.s_Language;
                        if (string.IsNullOrEmpty(Lang))
                        {
                            Lang = PlayerPrefs.GetString(nameof(Localization.Language), "English");
                        }
                        Russian = Lang == "Russian";
                    }
                    catch (Exception)
                    {
                        Russian = false;
                    }
                }
                return Russian;
            }
        }

        private static readonly Dictionary<string, string> Exact = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Main and multiplayer menus
            { "MULTIPLAYER", "Мультиплеер" },
            { "LOBBY", "Лобби" },
            { "HOST SERVER", "Создать сервер" },
            { "JOIN SERVER", "Подключиться" },
            { "SETTINGS", "Настройки" },
            { "BROWSE SERVERS", "Список серверов" },
            { "CONNECT BY IP", "Подключиться по IP" },
            { "CONNECT TO FRIEND", "Подключиться к другу" },
            { "CHANGE NICKNAME", "Сменить ник" },
            { "CUSTOMIZATION", "Внешний вид" },
            { "COPY ACCOUNT ID", "Копировать ID аккаунта" },
            { "START SERVER", "Запустить сервер" },
            { "SELECT SAVE", "Выбрать сохранение" },
            { "VOTE", "Голосовать" },
            { "INVITE FRIEND", "Пригласить друга" },
            { "COPY INVITE LINK", "Копировать ссылку-приглашение" },
            { "NEW GAME", "Новая игра" },
            { "LOAD SAVE", "Загрузить сохранение" },
            { "FEATS", "Умения" },
            { "START VOTE", "Начать голосование" },
            { "CHOOSE MYSELF", "Выбрать самому" },
            { "REGION", "Регион" },
            { "EXPERIENCE MODE", "Сложность" },
            { "INVITE", "Пригласить" },
            { "INVITE (NO LOBBY)", "Пригласить (нет лобби)" },
            { "INVITE (STEAM ONLY)", "Пригласить (только Steam)" },

            // Menu descriptions
            { "People who supported the project.", "Люди, которые поддержали проект." },
            { "Host or join a multiplayer session.", "Создать сетевую игру или подключиться к ней." },
            { "Show lobby you are currently in.", "Показать лобби, в котором вы сейчас." },
            { "Invite your steam friend to this P2P session. Once you invite someone, they will join the lobby first and then will be connected to your server.", "Пригласить друга из Steam в эту P2P-игру. Приглашённый сначала попадёт в лобби, а затем подключится к вашему серверу." },
            { "Configure and host a session.", "Настроить и запустить сервер." },
            { "Find a server or join by IP address.", "Найти сервер или подключиться по IP-адресу." },
            { "Join by IP address.", "Подключиться по IP-адресу." },
            { "Change your nickname and customize your appeal.", "Сменить ник и настроить внешний вид." },
            { "Browse public servers.", "Посмотреть публичные серверы." },
            { "Connect to a server by IP address.", "Подключиться к серверу по IP-адресу." },
            { "Opens steam friends overlay.", "Открыть список друзей Steam." },
            { "Change your character name. Other players will see it ingame.", "Сменить имя персонажа. Другие игроки увидят его в игре." },
            { "Toggle supporter bonuses and select flairs.", "Включить бонусы поддержавших и выбрать значки." },
            { "Copy to clipboard EGS or Steam Account ID.", "Скопировать ID аккаунта Steam или EGS в буфер обмена." },
            { "Start your session if everyone is ready, or if you do not want to wait for other players.\n(Players will be able to join, even after the session is already started).", "Начать игру, если все готовы или вы не хотите ждать остальных.\n(Игроки смогут подключиться и после начала игры)." },
            { "Create or load a sandbox game.", "Создать или загрузить игру в режиме выживания." },
            { "Vote for session settings.", "Голосовать за настройки игры." },
            { "Invite your steam friend to this lobby.", "Пригласить друга из Steam в это лобби." },
            { "Copy invite link to clipboard.", "Скопировать ссылку-приглашение в буфер обмена." },
            { "Start vote for session settings.", "Начать голосование за настройки игры." },
            { "Create new sandbox game without voting.", "Создать новую игру без голосования." },
            { "Vote for a starting region.", "Голосовать за стартовый регион." },
            { "Vote for experience mode.", "Голосовать за сложность." },

            // Popups
            { "Input server address", "Введите адрес сервера" },
            { "Connect", "Подключиться" },
            { "Next", "Далее" },
            { "HOW DO YOU WANT TO BE CALLED?", "Как вас называть?" },
            { "Your ID was copied to clipboard", "Ваш ID скопирован в буфер обмена" },
            { "Can't detect your account ID", "Не удалось определить ID аккаунта" },
            { "Server browser is unavailable (asset bundle not loaded). Use CONNECT BY IP.", "Список серверов недоступен (не загружен файл ассетов). Используйте «Подключиться по IP»." },
            { "Link copied to clipboard.", "Ссылка скопирована в буфер обмена." },
            { "Connecting...", "Подключение..." },
            { "Connecting", "Подключение" },
            { "Trying to join...", "Подключаемся..." },
            { "Please wait a moment...", "Подождите немного..." },
            { "Please wait, if you using hamachi/radmin or any other lan emulator and you have connection problems, please don't ask us for help.", "Подождите. Если вы используете Hamachi, Radmin или другой эмулятор локальной сети и не можете подключиться, авторы мода с этим не помогут." },
            { "Connection failed", "Не удалось подключиться" },
            { "The server is not responding.", "Сервер не отвечает." },
            { "Server respond incorrect data, make sure client and server use same version of the mod.", "Сервер прислал неверные данные. Убедитесь, что у вас и у хоста одна версия мода." },
            { "Server is full!", "Сервер заполнен!" },
            { "Wasn't able to reach server.", "Не удалось связаться с сервером." },
            { "DISCONNECTED", "Отключено" },
            { "DISCONNECT TIMEOUT", "Связь потеряна" },
            { "Host did not responced after 10 Attempts, you can safly exit the game.", "Хост не ответил после 10 попыток, можно спокойно выходить из игры." },
            { "Game won't be saves to keep save valid for host on reconnection!", "Игра не будет сохранена, чтобы при переподключении сохранение у хоста осталось верным!" },
            { "You has been banned from the server.", "Вы забанены на этом сервере." },
            { "Outdated RCON protocol!", "Устаревший протокол RCON!" },
            { "Wrong mods installed! You have that different from server's mods, or you missing some mods that installed on the server, check logs!", "Не те моды! Ваши моды отличаются от модов сервера или каких-то не хватает, смотрите лог!" },
            { "The host has disconnected you from the server due to a long period without receiving data from you.", "Хост отключил вас: от вас долго не приходили данные." },
            { "Your RCON session is over, please reconnect.", "Сессия RCON завершена, переподключитесь." },
            { "Can't join this lobby", "Не удалось войти в лобби" },
            { "Server is no more available to join", "Сервер больше недоступен для подключения" },
            { "YOU ALREADY ON SERVER", "Вы уже на сервере" },
            { "You already on the server, restart the game if you want to join to another server", "Вы уже на сервере. Чтобы зайти на другой, перезапустите игру" },
            { "ERROR", "Ошибка" },
            { "Sorry nickname should not contain non latin symbols", "Ник должен состоять из латинских букв" },
            { "You can't use custom experience mode in online! This case to major desync!", "Свою сложность в сетевой игре использовать нельзя: это ломает синхронизацию!" },
            { "Open flairs customization menu?", "Открыть настройку значков?" },
            { "Please wait...", "Подождите..." },
            { "Please wait..", "Подождите..." },
            { "Saving", "Сохранение" },
            { "Saving before exit...", "Сохраняемся перед выходом..." },
            { "Verifying...", "Проверка..." },
            { "Host checking my save file...", "Хост проверяет сохранение..." },
            { "Sending container data...", "Отправка содержимого ящика..." },
            { "Downloading container data...", "Загрузка содержимого ящика..." },
            { "Downloading scene drops...", "Загрузка брошенных вещей..." },
            { "Downloading gear...", "Загрузка предмета..." },
            { "Downloading Gear", "Загрузка предмета" },
            { "Downloading animal carcass", "Загрузка туши животного" },
            { "Trying to pickup little thing", "Подбираем предмет" },
            { "Preparing to dismantle...", "Подготовка к разборке..." },
            { "Locking door...", "Запираем дверь..." },
            { "Trying open this door...", "Открываем дверь..." },
            { "Trying unlock this door...", "Отпираем дверь..." },
            { "Looking through the peephole", "Смотрим в глазок" },
            { "Coordinating...", "Согласование..." },
            { "Looking in pockets...", "Ищем в карманах..." },
            { "Calling to coordinator...", "Связываемся с координатором..." },
            { "Requesting data about blank...", "Запрос данных о заготовке..." },
            { "Host received invalid data", "Хост получил неверные данные" },
            { "Trying send data again...", "Отправляем данные ещё раз..." },
            { "Waiting for server restart", "Ждём перезапуска сервера" },
            { "Request failed", "Запрос не выполнен" },
            { "Diagnosis player", "Осмотр игрока" },

            // HUD messages
            { "THIS ALREADY PICKED!", "Это уже подобрали!" },
            { "Nothing to harvest", "Нечего собирать" },
            { "Someone already work on this!", "Этим уже кто-то занят!" },
            { "Somebody's knocking on the door", "Кто-то стучит в дверь" },
            { "Rock cache should be empty!", "Тайник должен быть пустым!" },
            { "Feedback form opened in your browser", "Форма отзыва открыта в браузере" },
            { "Can't pick this up", "Это нельзя подобрать" },
            { "Can't interact with this", "С этим нельзя взаимодействовать" },
            { "DISCONNECTED FROM SERVER", "Отключено от сервера" },
            { "Diagnosis failed!", "Осмотр не удался!" },
            { "Request failed!", "Запрос не выполнен!" },
            { "You already have technical backpack!", "У вас уже есть технический рюкзак!" },
            { "YOU ALREADY CONNECTED!!!!", "Вы уже подключены!" },
            { "You need whetstone!", "Нужен точильный камень!" },
            { "You need hacksaw!", "Нужна ножовка!" },
            { "You need liquid lead!", "Нужен расплавленный свинец!" },
            { "This key already used for other door!", "Этот ключ уже от другой двери!" },
            { "This door is already locked!", "Дверь уже заперта!" },
            { "This door can't be locked!", "Эту дверь нельзя запереть!" },
            { "The key broke!", "Ключ сломался!" },
            { "Incorrect key!", "Не тот ключ!" },
            { "You have not any invites.", "У вас нет приглашений." },
            { "No body knocking.", "Никто не стучит." },
            { "You don't have any keys to lock this door!", "У вас нет ключей, чтобы запереть эту дверь!" },
            { "YOU CAN'T DROP THIS", "Это нельзя выбросить" },
            { "You are entering safe zone!", "Вы входите в безопасную зону!" },
            { "You are leaving safe zone!", "Вы покидаете безопасную зону!" },
            { "You cannot attack when you in the safe zone!", "В безопасной зоне нападать нельзя!" },
            { "You cannot attack players in the safe zone!", "Нельзя нападать на игроков в безопасной зоне!" },
            { "Invalid ID of player!", "Неверный ID игрока!" },
            { "You and player on different scenes!", "Вы с игроком в разных локациях!" },
            { "WAIT FOR EVERYONE", "Ждём всех" },
            { "WAITING FOR OTHER PLAYERS TO SLEEP", "Ждём, пока остальные уснут" },
            { "AFFLICTION TREATED\nPATIENT NEEDS TIME TO HEAL", "Недуг вылечен\nПациенту нужно время на восстановление" },
            { "Gives ability to know your sanity status.", "Позволяет знать своё психическое состояние." },
            { "Crash site has been found!", "Место крушения найдено!" },
            { "Time is up, no one has found the crash site.", "Время вышло, место крушения никто не нашёл." },
            { "Youn found [50C878]special item[-] use it to start advanced expedition via handheld radio.", "Вы нашли [50C878]особый предмет[-]: с ним можно начать сложную экспедицию через рацию." },
            { "You don't have any [50C878]special items[-] to start advanced expedition.", "У вас нет [50C878]особых предметов[-] для сложной экспедиции." },

            // Chat
            { "[Global] ", "[Всем] " },
            { "[Area] ", "[Рядом] " },
            { "[Private] ", "[Лично] " },
            { "Enter text...", "Введите текст..." },
            { "Welcome to chat!", "Добро пожаловать в чат!" },

            // Bundle UI: host settings
            { "Items duplication", "Дублирование предметов" },
            { "Containers duplication", "Дублирование ящиков" },
            { "Spawn style", "Место появления" },
            { "Players", "Игроков" },
            { "Steam P2P", "Steam P2P" },
            { "Public server", "Публичный сервер" },
            { "Close", "Закрыть" },
            { "Host", "Создать" },
            { "Port", "Порт" },
            { "Default is 26950", "По умолчанию 26950" },
            { "Fire Sync", "Синхронизация огня" },
            { "Cheats", "Читы" },
            { "Availability", "Доступ" },
            { "Same as host", "Как у хоста" },
            { "Client's choice", "Выбор игрока" },
            { "Random", "Случайно" },
            { "Fixed place", "Фиксированное место" },
            { "Disabled", "Выключено" },
            { "Visual", "Только видно" },
            { "Share", "Общее топливо" },
            { "Host only", "Только хост" },
            { "Allow to everyone", "Всем" },
            { "Invites only", "Только по приглашениям" },
            { "Friends only", "Только друзья" },
            { "Public", "Публичный" },
            { "Please don't ask for help with LAN emulation or port forwarding!!! We not your internet provider, we only develope this mod.", "Пожалуйста, не просите авторов мода помочь с эмуляцией локальной сети или пробросом портов: они делают только мод." },
            { "Item duplication.\n\nIf disabled - When someone pick up any natural spawned item, item will be destroyed for everyone other, It doesn't matter if this item was picked up before the other players entered the server or while it they was on the server, or if they was in this region at all.\n\nIf enabled - Items does not destroy for other players, so an natural spawned item can be picked by every player, even if they join server after.",
              "Дублирование предметов.\n\nВыключено: если кто-то подобрал предмет, лежавший в мире, он пропадает у всех остальных, даже если они зашли на сервер позже или не были в этом регионе.\n\nВключено: предмет не пропадает у других, и каждый игрок может подобрать свой, даже если зашёл позже." },
            { "Containers duplication.\n\nIf disabled - When someone open any container spawned items inside will be destroyed for everyone other, It doesn't matter if this happen before the other players entered the server or while it they was on the server, or if they was in this region at all.\n\nIf enabled - spawned items inside containers does not destroy for other players, so items inside containers can be looted by every player, even if they join server after.",
              "Дублирование ящиков.\n\nВыключено: если кто-то открыл ящик, его содержимое пропадает у всех остальных, даже если они зашли на сервер позже или не были в этом регионе.\n\nВключено: содержимое ящиков не пропадает у других, и каждый игрок может обыскать их сам, даже если зашёл позже." },
            { "Where joined players will spawn \n\nSame as host - Region where host is currently is, but random position in limit of region.\n\nClient's choice - Client select region for spawn.\n\nRandom - Random region, random position.\n\nFixed place - The spawn location will be just the same at all times for everyone (including region, and position). But it is selected once when starting the server for the first time.",
              "Где появляются подключившиеся игроки.\n\nКак у хоста: в регионе, где сейчас хост, в случайном месте.\n\nВыбор игрока: игрок сам выбирает регион.\n\nСлучайно: случайный регион и место.\n\nФиксированное место: всегда одно и то же место для всех (регион и точка). Выбирается один раз при первом запуске сервера." },
            { "Fire synchronization \n\nDisabled - No any Fire sync at all. everyone has own fire.\n\nVisual - If any player make campfire or start fire add ovens you will see fire, but will not get any warming it from until at own fuel to it.\n\nShare - Same as Visual, but if someone added fuel to fire, you will get same amout of fuel to your fire too. (But total fuel is not same for everyone, because you waste your fuel by your time skips and sleeping). If your fuel is over, it become same only visual like in Visual option.",
              "Синхронизация огня.\n\nВыключено: огонь не синхронизируется, у каждого свой.\n\nТолько видно: вы видите чужие костры и печи, но не греетесь, пока не подбросите своё топливо.\n\nОбщее топливо: как «Только видно», но когда кто-то подбрасывает топливо, у вас его тоже прибавляется. Запас у всех разный, потому что сон и пропуск времени тратят ваше топливо. Когда ваше топливо кончится, огонь станет просто видимым." },
            { "Cheats (Console commands)\n\nDisabled - No one can use cheat commands.\nHost only - Only server owner can use cheat commands.\nAllow to everyone - Any player can use cheat commands",
              "Читы (консольные команды).\n\nВыключено: никто не может пользоваться читами.\nТолько хост: читы доступны только хосту.\nВсем: читы доступны всем игрокам." },
            { "Availability\n\nInvites only - Only you can invite players to the server.\nFriends only - Any your friend from steam can join to the server without invites needed.\nPublic - Anyone can join to the server from server browser.",
              "Доступ.\n\nТолько по приглашениям: игроков приглашаете только вы.\nТолько друзья: любой ваш друг из Steam может зайти без приглашения.\nПубличный: зайти может любой из списка серверов." },
            { "Ports to host your server.\n\nIf you leave ports as 26950 players will not need to input ports when connecting by ip. \n\nBut if you set custom ports, clients will need to specify ports when they connecting like this ip:ports (example 127.0.0.1:7777).\n\n(If you not know what is it or what are you doing, don't touch it!)",
              "Порт сервера.\n\nЕсли оставить 26950, игрокам не нужно указывать порт при подключении по IP.\n\nЕсли указать другой, игрокам придётся писать его через двоеточие, например 127.0.0.1:7777.\n\n(Если не знаете, что это, не трогайте!)" },

            // Bundle UI: lobby, browser, customization
            { "Profile", "Профиль" },
            { "Kick", "Выгнать" },
            { "SAVE FILE", "Сохранение" },
            { "Condition:", "Состояние:" },
            { "Experience:", "Сложность:" },
            { "Survived:", "Прожито:" },
            { "Region:", "Регион:" },
            { "Location:", "Место:" },
            { "Seed:", "Сид:" },
            { "Exprience", "Сложность" },
            { "Join", "Войти" },
            { "Server browser", "Список серверов" },
            { "YOUR FRIENDS", "Ваши друзья" },
            { "Flairs", "Значки" },
            { "SELECTED", "Выбрано" },
            { "Warmth", "Тепло" },
            { "Thirst", "Жажда" },
            { "Hunger", "Голод" },
            { "Fatigue", "Усталость" },
            { "Sprint", "Бег" },
        };

        private static readonly List<KeyValuePair<Regex, string>> Patterns = new List<KeyValuePair<Regex, string>>
        {
            P(@"^(.+) IS BREAKING THIS!?$", "$1 ЛОМАЕТ ЭТО"),
            P(@"^(.+) IS USING THIS!?$", "$1 ИСПОЛЬЗУЕТ ЭТО"),
            P(@"^(.+) IS ALREADY COLLECTING THIS$", "$1 УЖЕ СОБИРАЕТ ЭТО"),
            P(@"^(.+) IS BUSY WITH THIS ALREADY$", "$1 УЖЕ ЗАНИМАЕТСЯ ЭТИМ"),
            P(@"^(.+) INSIDE, CAN'T DISMANTLE THIS!$", "ВНУТРИ ИГРОК $1, РАЗОБРАТЬ НЕЛЬЗЯ!"),
            P(@"^(.+) INTERACTING WITH THIS!$", "$1 ВЗАИМОДЕЙСТВУЕТ С ЭТИМ!"),
            P(@"^PLEASE DON'T MOVE, (.+) IS TENDING YOU$", "НЕ ДВИГАЙТЕСЬ, ВАС ЛЕЧИТ $1"),
            P(@"^(.+) MOVED, ACTION CANCELED$", "ДЕЙСТВИЕ ОТМЕНЕНО: $1 ДВИГАЕТСЯ"),
            P(@"^Requires (.+) and (.+)$", "Нужно: $1 и $2"),
            P(@"^Requires (.+)$", "Нужно: $1"),
            P(@"^Other player gave you (.+)\.$", "Другой игрок передал вам: $1."),
            P(@"^You gave (.+) to (.+)$", "Вы передали игроку $2: $1"),
            P(@"^You has been teleported to (.+)$", "Вы телепортированы к игроку $1"),
            P(@"^(.+) join the server$", "$1 на сервере"),
            P(@"^(.+) disconnected!$", "$1 отключается от сервера"),
            P(@"^(.+) changed name to (.+)$", "$1 теперь $2"),
            P(@"^(.+) can't join because has different mod version!$", "$1 не может зайти: другая версия мода!"),
            P(@"^(.+) can't join because has diferent mods installed!$", "$1 не может зайти: установлены другие моды!"),
            P(@"^Server using (.+) version of the mod!$", "На сервере версия мода $1!"),
            P(@"^Wrong mod version! Server using version (.+)$", "Не та версия мода! На сервере версия $1"),
            P(@"^Your version of the mod, isn't supported$", "Ваша версия мода не поддерживается"),
            P(@"^Host using version (.+)$", "У хоста версия $1"),
            P(@"^Reason: (.+)$", "Причина: $1"),
            P(@"^Ping Attempt #(.+)$", "Попытка связи №$1"),
            P(@"^Something wrong with #(.+) request, please reconnect to the server!$", "Ошибка запроса №$1, переподключитесь к серверу!"),
            P(@"^Sorry (.+) should not contain _ symbol$", "$1 не должно содержать символ _"),
            P(@"^Sorry (.+) should not contain non latin symbols$", "$1 должно состоять из латинских букв"),
            P(@"^Players: (.+)$", "Игроки: $1"),
            P(@"^Status: (.+)$", "Статус: $1"),
            P(@"^State: (.+)$", "Статус: $1"),
            P(@"^LOBBY (.+)$", "ЛОББИ $1"),
            P(@"^VOTING: (.+)$", "ГОЛОСОВАНИЕ: $1"),
            P(@"^Flair Slot #(.+)$", "Слот значка №$1"),
            P(@"^Plane with a valuable cargo crashed somewhere on (.+), find the crash site before other players do\.$", "Самолёт с ценным грузом разбился где-то в регионе $1. Найдите место крушения раньше других."),
        };

        private static KeyValuePair<Regex, string> P(string pattern, string replacement)
        {
            return new KeyValuePair<Regex, string>(new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), replacement);
        }

        private static bool IsUpper(string s)
        {
            bool HasLetter = false;
            foreach (char c in s)
            {
                if (char.IsLetter(c))
                {
                    HasLetter = true;
                    if (char.IsLower(c))
                    {
                        return false;
                    }
                }
            }
            return HasLetter;
        }

        private static string TranslateLine(string line)
        {
            string Core = line.Trim();
            if (Core.Length == 0)
            {
                return line;
            }
            string Result = null;
            if (!Exact.TryGetValue(Core, out Result))
            {
                foreach (KeyValuePair<Regex, string> Pattern in Patterns)
                {
                    if (Pattern.Key.IsMatch(Core))
                    {
                        Result = Pattern.Key.Replace(Core, Pattern.Value);
                        break;
                    }
                }
            }
            if (Result == null)
            {
                return line;
            }
            // Keep the source's caps style, so ALL-CAPS HUD lines stay ALL-CAPS.
            if (IsUpper(Core))
            {
                Result = Result.ToUpperInvariant();
            }
            int Lead = line.Length - line.TrimStart().Length;
            int Trail = line.Length - line.TrimEnd().Length;
            return line.Substring(0, Lead) + Result + line.Substring(line.Length - Trail);
        }

        // Returns the Russian text for an English mod string, or the string unchanged.
        public static string T(string text)
        {
            if (string.IsNullOrEmpty(text) || !IsRussian)
            {
                return text;
            }
            try
            {
                string Whole = TranslateLine(text);
                if (!ReferenceEquals(Whole, text) || text.IndexOf('\n') < 0)
                {
                    return Whole;
                }
                // Multi-line text with no whole-text entry: translate it line by line.
                string[] Lines = text.Split('\n');
                for (int i = 0; i < Lines.Length; i++)
                {
                    Lines[i] = TranslateLine(Lines[i]);
                }
                return string.Join("\n", Lines);
            }
            catch (Exception)
            {
                return text;
            }
        }

        // For code that recognises a popup by its title: true for the English title, or its
        // translation in any letter case.
        public static bool Is(string shown, string english)
        {
            if (shown == null)
            {
                return false;
            }
            if (string.Equals(shown, english, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            string Translated;
            return Exact.TryGetValue(english.Trim(), out Translated) && string.Equals(shown.Trim(), Translated, StringComparison.OrdinalIgnoreCase);
        }

        private static readonly HashSet<string> TranslatedPrefabs = new HashSet<string>();

        // Bundle prefabs are shared by every instance, so translating the prefab once covers all of them.
        public static void TranslatePrefab(GameObject prefab)
        {
            if (prefab == null || !IsRussian || !TranslatedPrefabs.Add(prefab.name))
            {
                return;
            }
            try
            {
                foreach (UnityEngine.UI.Text Label in prefab.GetComponentsInChildren<UnityEngine.UI.Text>(true))
                {
                    if (Label != null && !string.IsNullOrEmpty(Label.text))
                    {
                        string Source = Label.text;
                        Label.text = T(Source);
                        // Russian runs longer than the English the panels were laid out for, and
                        // these labels truncate; let a longer label shrink to fit instead.
                        if (Label.text.Length > Source.Length && !Label.resizeTextForBestFit)
                        {
                            Label.resizeTextMaxSize = Label.fontSize;
                            Label.resizeTextMinSize = Math.Max(6, Label.fontSize / 2);
                            Label.resizeTextForBestFit = true;
                        }
                    }
                }
                foreach (UnityEngine.UI.Dropdown Drop in prefab.GetComponentsInChildren<UnityEngine.UI.Dropdown>(true))
                {
                    if (Drop == null || Drop.options == null)
                    {
                        continue;
                    }
                    for (int i = 0; i < Drop.options.Count; i++)
                    {
                        UnityEngine.UI.Dropdown.OptionData Option = Drop.options[i];
                        if (Option != null && !string.IsNullOrEmpty(Option.text))
                        {
                            Option.text = T(Option.text);
                        }
                    }
                    Drop.RefreshShownValue();
                }
            }
            catch (Exception e)
            {
                MelonLoader.MelonLogger.Warning("[Loc] Could not translate " + prefab.name + ": " + e.Message);
            }
        }

        // The patches below only see calls made from managed code, which here means the mod's own
        // calls; the game calls these natively with localization ids, so they are left alone.
        [HarmonyPatch]
        internal static class HUDMessage_AddMessage
        {
            private static IEnumerable<MethodBase> TargetMethods()
            {
                foreach (MethodInfo M in typeof(HUDMessage).GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (M.Name == "AddMessage" && M.GetParameters().Length > 0 && M.GetParameters()[0].ParameterType == typeof(string))
                    {
                        yield return M;
                    }
                }
            }

            private static void Prefix(ref string message)
            {
                message = T(message);
            }
        }

        [HarmonyPatch]
        // Overloads of a method whose parameters include all of "with" and none of "without".
        // Writing to __args is not carried back into Il2Cpp wrapper calls, so each patch takes the
        // strings as named ref parameters and is aimed only at overloads that have them.
        private static IEnumerable<MethodBase> Overloads(Type type, string name, string[] with, string[] without)
        {
            foreach (MethodInfo M in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                if (M.Name != name)
                {
                    continue;
                }
                HashSet<string> Names = new HashSet<string>();
                foreach (ParameterInfo Param in M.GetParameters())
                {
                    Names.Add(Param.Name);
                }
                if (Array.TrueForAll(with, Names.Contains) && !Array.Exists(without, Names.Contains))
                {
                    yield return M;
                }
            }
        }

        [HarmonyPatch]
        internal static class Panel_Confirmation_TitleMessage
        {
            private static IEnumerable<MethodBase> TargetMethods()
            {
                return Overloads(typeof(Panel_Confirmation), "AddConfirmation", new[] { "titleLocID", "messageLocID" }, new string[0]);
            }

            private static void Prefix(ref string titleLocID, ref string messageLocID)
            {
                titleLocID = T(titleLocID);
                messageLocID = T(messageLocID);
            }
        }

        [HarmonyPatch]
        internal static class Panel_Confirmation_TitleButtons
        {
            private static IEnumerable<MethodBase> TargetMethods()
            {
                return Overloads(typeof(Panel_Confirmation), "AddConfirmation", new[] { "titleLocID", "buttonLocID1", "buttonLocID2" }, new[] { "messageLocID" });
            }

            private static void Prefix(ref string titleLocID, ref string buttonLocID1, ref string buttonLocID2)
            {
                titleLocID = T(titleLocID);
                buttonLocID1 = T(buttonLocID1);
                buttonLocID2 = T(buttonLocID2);
            }
        }

        [HarmonyPatch]
        internal static class Panel_Confirmation_Message
        {
            private static IEnumerable<MethodBase> TargetMethods()
            {
                return Overloads(typeof(Panel_Confirmation), "AddConfirmation", new[] { "messageLocID" }, new[] { "titleLocID" });
            }

            private static void Prefix(ref string messageLocID)
            {
                messageLocID = T(messageLocID);
            }
        }

        [HarmonyPatch]
        internal static class BasicMenu_UpdateTitle
        {
            private static IEnumerable<MethodBase> TargetMethods()
            {
                return Overloads(typeof(BasicMenu), "UpdateTitle", new[] { "localizedTextId" }, new string[0]);
            }

            private static void Prefix(ref string localizedTextId)
            {
                localizedTextId = T(localizedTextId);
            }
        }

        [HarmonyPatch]
        internal static class BasicMenu_UpdateTitleWithHeader
        {
            private static IEnumerable<MethodBase> TargetMethods()
            {
                return Overloads(typeof(BasicMenu), "UpdateTitle", new[] { "mainLocalizedTextId" }, new string[0]);
            }

            private static void Prefix(ref string mainLocalizedTextId)
            {
                mainLocalizedTextId = T(mainLocalizedTextId);
            }
        }
    }
}
