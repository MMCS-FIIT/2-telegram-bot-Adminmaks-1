using System.Reflection.Metadata.Ecma335;

namespace SimpleTGBot;

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

public class DatasetCharacter
{
    public int Armor { get; set; }
    public int Weapon { get; set; }
    public int Physical { get; set; }
    public int Magic { get; set; }
    public int Level { get; set; }
    public bool FBoss { get; set; }
    public string Class { get; set; } = "";
}
public class Character
{
    public string Name { get; set; } = "";
    public string Race { get; set; } = "";
    public string Class { get; set; } = "";
    public int Armor { get; set; }
    public int Weapon { get; set;  }
    public int Physical {  get; set; }
    public int Magic { get; set; }
    public int Level { get; set; }
    public bool IsBoss { get; set; }
}
public class TelegramBot
{
    // Токен TG-бота. Можно получить у @BotFather
    private const string BotToken = "ВАШ_ТОКЕН";

    private List<DatasetCharacter> _dataset = new();
    private ConcurrentDictionary<long, Character?> _currentCharacter = new();
    private ConcurrentDictionary<long, bool> _waitingForName = new();
    private Random _rnd = new();

    
    public async Task Run()
    {


        // Если вам нужно хранить какие-то данные во время работы бота (массив информации, логи бота,
        // историю сообщений для каждого пользователя), то это всё надо инициализировать в этом методе.
        // TODO: Инициализация необходимых полей

        LoadDataset("RPG.csv");
        // Инициализируем наш клиент, передавая ему токен.
        var botClient = new TelegramBotClient(BotToken);
        

        // Служебные вещи для организации правильной работы с потоками
        using CancellationTokenSource cts = new CancellationTokenSource();
        
        // Разрешённые события, которые будет получать и обрабатывать наш бот.
        // Будем получать только сообщения. При желании можно поработать с другими событиями.
        ReceiverOptions receiverOptions = new ReceiverOptions()
        {
            AllowedUpdates = new [] { UpdateType.Message }
        };

        // Привязываем все обработчики и начинаем принимать сообщения для бота
        botClient.StartReceiving(
            updateHandler: OnMessageReceived,
            pollingErrorHandler: OnErrorOccured,
            receiverOptions: receiverOptions,
            cancellationToken: cts.Token
        );

        // Проверяем что токен верный и получаем информацию о боте
        var me = await botClient.GetMeAsync(cancellationToken: cts.Token);
        Console.WriteLine($"Бот @{me.Username} запущен.\nДля остановки нажмите клавишу Esc...");
        
        // Ждём, пока будет нажата клавиша Esc, тогда завершаем работу бота
        while (Console.ReadKey().Key != ConsoleKey.Escape){}

        // Отправляем запрос для остановки работы клиента.
        cts.Cancel();
    }
    private void LoadDataset(string path)
    {
        if (!System.IO.File.Exists(path))
        {
            Console.WriteLine($"Файл {path} не найден!");
            return;
        }

        var lines = System.IO.File.ReadAllLines(path);
        for (int i = 1; i < lines.Length; i++) { 
            var parts = lines[i].Split(',');
            if (parts.Length < 7) continue;

            try
            {
                _dataset.Add(new DatasetCharacter
                {
                    Armor = int.Parse(parts[0]),
                    Weapon = int.Parse(parts[1]),
                    Physical = int.Parse(parts[2]),
                    Magic = int.Parse(parts[3]),
                    Level = int.Parse(parts[4]),
                    FBoss = bool.Parse(parts[5]),
                    Class = parts[6]
                });
            }
            catch (Exception ex) 
            {
                Console.WriteLine($"Ошибка строки {i}: {ex.Message}");
            }
        }

        Console.WriteLine($"Загружено {_dataset.Count} записей");
    }

    /// <summary>
    /// Обработчик события получения сообщения.
    /// </summary>
    /// <param name="botClient">Клиент, который получил сообщение</param>
    /// <param name="update">Событие, произошедшее в чате. Новое сообщение, голос в опросе, исключение из чата и т. д.</param>
    /// <param name="cancellationToken">Служебный токен для работы с многопоточностью</param>
    async Task OnMessageReceived(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        

        // Работаем только с сообщениями. Остальные события игнорируем
        var message = update.Message;
        if (message is null)
        {
            return;
        }
        // Будем обрабатывать только текстовые сообщения.
        // При желании можно обрабатывать стикеры, фото, голосовые и т. д.
        //
        // Обратите внимание на использованную конструкцию. Она эквивалентна проверке на null, приведённой выше.
        // Подробнее об этом синтаксисе: https://medium.com/@mattkenefick/snippets-in-c-more-ways-to-check-for-null-4eb735594c09
        if (message.Text is not { } messageText)
        {
            return;
        }

        // Получаем ID чата, в которое пришло сообщение. Полезно, чтобы отличать пользователей друг от друга.
        var chatId = message.Chat.Id;
        if (_waitingForName.TryGetValue(chatId, out var waiting) && waiting)
        {
            _waitingForName[chatId] = false;

            string name = messageText.ToLower() == "рандом"
                ? GenerateName()
                : messageText;

            if (_dataset.Count == 0)
            {
                await botClient.SendTextMessageAsync(chatId,
                    "Датасет не загружен",
                    cancellationToken: cancellationToken);
                return;
            }

            var row = _dataset[_rnd.Next(_dataset.Count)];
            var character = GenerateCharacter(row);
            character.Name = name;

            _currentCharacter[chatId] = character;

            await botClient.SendTextMessageAsync(
                chatId,
                "⚔ Твой персонаж готов:\n\n" + FormatCharacter(character),
                cancellationToken: cancellationToken);

            await botClient.SendTextMessageAsync(
                chatId,
                "Что хочешь сделать дальше?\n" +
                 "• /show — посмотреть персонажа\n" +
                 "• /boss — создать босса\n" +
                "• /create — создать нового",
                cancellationToken: cancellationToken);

            return;
        }

        // Печатаем на консоль факт получения сообщения
        Console.WriteLine($"Получено сообщение в чате {chatId}: '{messageText}'");

        // TODO: Обработка пришедших сообщений
        var text = messageText.ToLower();
        if (text == "/start")
        {
            var welcome = new StringBuilder();
            welcome.AppendLine("🧙‍♂️ *Добро пожаловать в RPG Character Forge!*");
            welcome.AppendLine();
            welcome.AppendLine("Я создаю уникальных RPG-персонажей на основе датасета ⚔");
            welcome.AppendLine();
            welcome.AppendLine("✨ Что я умею:");
            welcome.AppendLine("• /create — создать персонажа");
            welcome.AppendLine("• /boss — вызвать босса");
            welcome.AppendLine("• /show — показать последнего героя");
            welcome.AppendLine();
            welcome.AppendLine("💡 Также можешь написать:");
            welcome.AppendLine("`создай персонажа уровня 50`");
            welcome.AppendLine();
            welcome.AppendLine("Готов начать? 😏");
            await botClient.SendTextMessageAsync(chatId, welcome.ToString(),parseMode:ParseMode.Markdown, replyMarkup: GetMainKeyboard(), cancellationToken:cancellationToken);
            return;
        }
        if (text == "/create")
        {
          
            _waitingForName[chatId] = true;

            await botClient.SendTextMessageAsync(
                chatId,
                "✍ Введи имя для своего персонажа:",
                cancellationToken: cancellationToken);

            return;
        }
        if (text == "/boss")
        {
            var bosses = _dataset.Where(x => x.FBoss).ToList();
            if (bosses.Count == 0)
            {
                await botClient.SendTextMessageAsync(chatId, "Боссы не найдены", cancellationToken: cancellationToken);
                return;
            }
            var row = bosses[_rnd.Next(bosses.Count)];
            var character = GenerateCharacter(row,true);
            _currentCharacter[chatId] = character;

            await botClient.SendTextMessageAsync(chatId, 
               "БОСС!\n\n" + FormatCharacter(character), cancellationToken: cancellationToken);

            await botClient.SendTextMessageAsync(
               chatId,
               "Что хочешь сделать дальше?\n" +
                "• /show — посмотреть персонажа\n" +
                "• /boss — создать босса\n" +
               "• /create — создать нового",
               cancellationToken: cancellationToken);
            return;


        }
        if(text == "/show")
        {
            if (!_currentCharacter.TryGetValue(chatId, out var ch) || ch == null)
            {
                await botClient.SendTextMessageAsync(chatId, "Сначала создай персонажа.", cancellationToken: cancellationToken);
                return;
            }
            await botClient.SendTextMessageAsync(chatId, FormatCharacter(ch), cancellationToken: cancellationToken);
            return;

        }
        if (text == "рестарт" || text == "/restart")
        {
            _currentCharacter.TryRemove(chatId, out _);
            _waitingForName.TryRemove(chatId, out _);

            await botClient.SendTextMessageAsync(
                chatId,
                "🔄 Все данные очищены. Можешь начать заново!\nНапиши /create чтобы создать нового персонажа.",
                replyMarkup: GetMainKeyboard(),
                cancellationToken: cancellationToken);

            return;
        }


        var match = Regex.Match(text, @"уровня\s+(\d+)");
        if (match.Success && int.TryParse(match.Groups[1].Value, out int lvl))
        {
            var candidates = _dataset.OrderBy(x => Math.Abs(x.Level - lvl)).Take(20).ToList();
            if (candidates.Count == 0) return;
            
            var row = candidates[_rnd.Next(candidates.Count)];
            var character = GenerateCharacter(row);
            _currentCharacter[chatId] = character;

            await botClient.SendTextMessageAsync(chatId,$"Персонаж около уровня{lvl}:\n\n"+ FormatCharacter(character), cancellationToken: cancellationToken);
            return;
        }

        if (text == "/help")
        {

            await botClient.SendTextMessageAsync(chatId, "📜 Доступные команды:\n" +
        "/create — создать персонажа\n" +
        "/boss — вызвать босса\n" +
        "/show — показать последнего персонажа\n" +
        "Также можно написать: 'создай персонажа уровня 50'", cancellationToken: cancellationToken);
                
        }
        await botClient.SendTextMessageAsync(chatId, $"Не понимаю команду.Напиши /help",cancellationToken: cancellationToken);
        return;
    

    }

    private string GenerateName()
    {
        string[] names = {
        "Артос", "Леголас", "Гром", "Рагнар", "Элион",
        "Келдор", "Мортис", "Сигурд", "Тирион", "Валар"
    };

        return names[_rnd.Next(names.Length)];
    }
    private Character GenerateCharacter(DatasetCharacter row, bool isBoss = false)
    {
        string[] races = { "Человек", "Эльф", "Дворф", "Орк", "Полурослик", "Гном", "Тифлинг", "Драконорожденный", "Аазимар", "Голиаф", "Дженази", "Табакси", "Лизардфолк", "Кентавр", "Минотавр", "Ааракокра", "Гнолл", "Скавен", "Кованый", "Гоблин" }
;

        return new Character
        {
            Name = isBoss ? "Босс" : "Герой",
            Race = races[_rnd.Next(races.Length)],
            Class = row.Class,
            Armor = row.Armor,
            Weapon = row.Weapon,
            Physical = row.Physical,
            Magic = row.Magic,
            Level = row.Level,
            IsBoss = isBoss || row.FBoss
        };
    }

    private string FormatCharacter(Character ch) 
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Имя: {ch.Name}");
        sb.AppendLine($"Раса: {ch.Race}");
        sb.AppendLine($"Класс: {ch.Class}");
        sb.AppendLine($"Уровень: {ch.Level}");
        sb.AppendLine($"Сила: {ch.Physical}");
        sb.AppendLine($"Магия: {ch.Magic}");
        if (ch.IsBoss) sb.AppendLine("БОСС");
        return sb.ToString();
    }

    private IReplyMarkup GetMainKeyboard()
    {
        return new ReplyKeyboardMarkup(new[]
        {
        new KeyboardButton[] { "/create", "/boss" },
        new KeyboardButton[] { "/show", "/help" },
        new KeyboardButton[] { "/restart" }
    })
        {
            ResizeKeyboard = true
        };
    }
    /// <summary>
    /// Обработчик исключений, возникших при работе бота
    /// </summary>
    /// <param name="botClient">Клиент, для которого возникло исключение</param>
    /// <param name="exception">Возникшее исключение</param>
    /// <param name="cancellationToken">Служебный токен для работы с многопоточностью</param>
    /// <returns></returns>
    Task OnErrorOccured(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
    {
        // В зависимости от типа исключения печатаем различные сообщения об ошибке
        var errorMessage = exception switch
        {
            ApiRequestException apiRequestException
                => $"Telegram API Error:\n[{apiRequestException.ErrorCode}]\n{apiRequestException.Message}",
            
            _ => exception.ToString()
        };

        Console.WriteLine(errorMessage);
        
        // Завершаем работу
        return Task.CompletedTask;
    }
}