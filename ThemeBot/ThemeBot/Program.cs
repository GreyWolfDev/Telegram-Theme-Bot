using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.IO;
using System.Linq;
using System.Net.Mail;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ionic.Zip;
using Ionic.Zlib;
using Microsoft.Win32;
using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.InlineQueryResults;
using Telegram.Bot.Types.InputMessageContents;
using Telegram.Bot.Types.ReplyMarkups;
using File = System.IO.File;

namespace ThemeBot
{
    class Program
    {
        public static TelegramBotClient Client;
        public static List<LUser> LocalUsers = new List<LUser>();
        internal static List<Dictionary<string, string>> CachedThemes = new List<Dictionary<string, string>>();
        internal static List<Theme> LoadedThemes = new List<Theme>();
        const long ThemeGroup = -191419236;
        internal static string RootDirectory
        {
            get
            {
                string codeBase = Assembly.GetExecutingAssembly().CodeBase;
                UriBuilder uri = new UriBuilder(codeBase);
                string path = Uri.UnescapeDataString(uri.Path);
                return Path.GetDirectoryName(path);
            }
        }

        internal static string ThemesDirectory = Path.GetFullPath(Path.Combine(RootDirectory, "Themes"));

        static void Main(string[] args)
        {
            new Thread(InitializeBot).Start();
            Thread.Sleep(-1);
        }

        private static void InitializeBot()
        {
            var api =
#if DEBUG
                "DebugAPI";
#else
            "ProductionAPI";
#endif
            Client = new TelegramBotClient(RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey("SOFTWARE\\ThemeBot").GetValue(api).ToString());
            Client.OnMessage += ClientOnOnMessage;
            Client.OnCallbackQuery += ClientOnOnCallbackQuery;
            Client.OnInlineQuery += ClientOnOnInlineQuery;
            Client.OnInlineResultChosen += ClientOnOnInlineResultChosen;
            Client.StartReceiving();
            CacheThemes();
        }

        private static void ClientOnOnInlineResultChosen(object sender, ChosenInlineResultEventArgs chosenInlineResultEventArgs)
        {

        }

        private static void ClientOnOnInlineQuery(object sender, InlineQueryEventArgs inlineQueryEventArgs)
        {
            try
            {
                var q = inlineQueryEventArgs.InlineQuery;
                CreateOrUpdateDBUser(q.From);
                var lu = GetLocalUser(q.From.Id);
                var search = q.Query.ToLower();
                if (lu.Search != search)
                {
                    lu.Search = search;
                    lu.Page = 0;
                    using (var db = new tdthemeEntities())
                    {
                        lu.ResultSet = LoadedThemes.ToList().Where(x => (x.Description.ToLower().Contains(search) || x.Name.ToLower().Contains(search))).ToList();
                    }
                    if (!lu.ResultSet.Any())
                    {
                        Client.AnswerInlineQueryAsync(q.Id,
                             new InlineQueryResult[0]);
                    }
                }
                var offset = 0;
                //display results - 5 at a time
                if (!String.IsNullOrEmpty(q.Offset))
                    offset = int.Parse(q.Offset);

                var toSend =
                    lu.ResultSet.Skip(offset)
                        .Take(5)
                        .Select(
#if !DEBUG
                        x => new InlineQueryResultCachedPhoto
                        {
                            Description = x.Description,
                            Caption = $"{x.Name}\n{x.Description}" + (x.ShowOwnerName ? $"\nBy {x.User.Name}" + (x.ShowOwnerUsername ? $" (@{x.User.Username})" : "") : (x.ShowOwnerUsername ? $"\nBy @{x.User.Username}" : "")),
                            FileId = x.Photo_Id,
                            Id = x.Id.ToString(),
                            //InputMessageContent = new InputTextMessageContent { MessageText = x.Description + "text", DisableWebPagePreview = true },
                            Title = x.Name,
                            ReplyMarkup = new InlineKeyboardMarkup(new[] { new InlineKeyboardButton("Get Theme") { Url = "https://t.me/tthemebot?start=t" + x.Id }, new InlineKeyboardButton("Rate") { Url = "https://t.me/tthemebot?start=r" + x.Id } })
                        }).ToArray();
#else
                    x => new InlineQueryResultArticle()
                    {
                        Description = x.Description,
                        Id = x.Id.ToString(),
                        InputMessageContent = new InputTextMessageContent { MessageText = $"{x.Name}\n{x.Description}" + (x.ShowOwnerName ? $"\nBy {x.User.Name}" + (x.ShowOwnerUsername ? $" (@{x.User.Username})" : "") : ""), DisableWebPagePreview = true },
                        Title = x.Name,
                        ReplyMarkup = new InlineKeyboardMarkup(new[] { new InlineKeyboardButton("Get Theme") { Url = "https://t.me/tthemebot?start=t" + x.Id }, new InlineKeyboardButton("Rate") { Url = "https://t.me/tthemebot?start=r" + x.Id }, })
                    }).ToArray();
#endif
                offset += 5;
                var result = Client.AnswerInlineQueryAsync(q.Id, toSend, nextOffset: offset.ToString()).Result;
            }
            catch (AggregateException e)
            {
                Client.SendTextMessageAsync(129046388, e.InnerExceptions[0].Message);
            }
            catch (Exception e)
            {
                while (e.InnerException != null)
                    e = e.InnerException;
                Client.SendTextMessageAsync(129046388, e.Message + "\n" + e.StackTrace);
            }
        }

        private static void ClientOnOnCallbackQuery(object sender, CallbackQueryEventArgs callbackQueryEventArgs)
        {
            try
            {
                var q = callbackQueryEventArgs.CallbackQuery;
                var lu = GetLocalUser(q.From.Id);
                Theme t;
                if (lu.QuestionAsked == QuestionType.ShowOwnerName && q.Data.StartsWith("showname"))
                {
                    if (lu.ThemeCreating != null)
                    {
                        lu.ThemeCreating.ShowOwnerName = q.Data.Split('|')[1] == "yes";
                        if (!String.IsNullOrEmpty(q.From.Username))
                        {
                            lu.QuestionAsked = QuestionType.ShowOwnerUsername;
                            Client.AnswerCallbackQueryAsync(q.Id, null, false, null, 0, default(CancellationToken));
                            Client.EditMessageTextAsync(q.From.Id, q.Message.MessageId,
                                $"Ok, do you want us to show your username? (@{q.From.Username})", replyMarkup:
                                    new InlineKeyboardMarkup(new[]
                                    {
                                        new InlineKeyboardButton("Yes", "showuser|yes"),
                                        new InlineKeyboardButton("No", "showuser|no")
                                    }));
                        }
                        else
                        {
                            Client.AnswerCallbackQueryAsync(q.Id, null, false, null, 0, default(CancellationToken));
                            Client.EditMessageTextAsync(q.From.Id, q.Message.MessageId,
                                "Ok, give me just a moment to upload your file...", replyMarkup: null);
                            lu.ThemeCreating.ShowOwnerUsername = false;
                            SaveTheme(lu);
                        }
                    }
                    else
                    {
                        lu.ThemeUpdating.ShowOwnerName = q.Data.Split('|')[1] == "yes";
                        if (!String.IsNullOrEmpty(q.From.Username))
                        {
                            lu.QuestionAsked = QuestionType.ShowOwnerUsername;
                            Client.AnswerCallbackQueryAsync(q.Id, null, false, null, 0, default(CancellationToken));
                            Client.EditMessageTextAsync(q.From.Id, q.Message.MessageId,
                                $"Ok, do you also want us to show your username? (@{q.From.Username})", replyMarkup:
                                    new InlineKeyboardMarkup(new[]
                                    {
                                        new InlineKeyboardButton("Yes", "showuser|yes"),
                                        new InlineKeyboardButton("No", "showuser|no")
                                    }));
                        }
                        else
                        {
                            Client.AnswerCallbackQueryAsync(q.Id, null, false, null, 0, default(CancellationToken));
                            Client.EditMessageTextAsync(q.From.Id, q.Message.MessageId,
                                "Ok, give me just a moment to upload your file...", replyMarkup: null);
                            lu.ThemeUpdating.ShowOwnerUsername = false;
                            SaveTheme(lu, true);
                        }

                    }
                    return;
                }
                if (lu.QuestionAsked == QuestionType.ShowOwnerUsername && q.Data.StartsWith("showuser"))
                {
                    if (lu.ThemeCreating != null)
                        lu.ThemeCreating.ShowOwnerUsername = q.Data.Split('|')[1] == "yes";
                    else
                        lu.ThemeUpdating.ShowOwnerUsername = q.Data.Split('|')[1] == "yes";
                    Client.AnswerCallbackQueryAsync(q.Id, null, false, null, 0, default(CancellationToken));
                    Client.EditMessageTextAsync(q.From.Id, q.Message.MessageId,
                        "Ok, give me just a moment to upload your file...", replyMarkup: null);
                    SaveTheme(lu, lu.ThemeCreating == null);
                    return;
                }
                var args = q.Data.Split('|');
                switch (args[0])
                {
                    //approval
                    case "app":
                        var app = args[2] == "yes";
                        using (var db = new tdthemeEntities())
                        {
                            var th = db.Themes.Find(int.Parse(args[1]));
                            th.Approved = app;
                            if (app)
                            {
                                Client.AnswerCallbackQueryAsync(q.Id, "Approved", false, null, 0);
                                Client.EditMessageCaptionAsync(q.Message.Chat.Id, q.Message.MessageId, $"Approved by: {q.From.FirstName}\n{q.Message.Caption}",
                                    null);
                                db.SaveChanges();
                            }
                            else
                            {
                                //ask for a reason
                                var menu = new InlineKeyboardMarkup(new[]
                                {
                                    new[]
                                    {
                                        new InlineKeyboardButton("Image", $"dis|{args[1]}|img"),
                                        new InlineKeyboardButton("Title / Description", $"dis|{args[1]}|text"),
                                    },
                                    new[]
                                    {
                                        new InlineKeyboardButton("Image and Title", $"dis|{args[1]}|imgtext"),
                                        new InlineKeyboardButton("Other", $"dis|{args[1]}|other"),
                                    }
                                });
                                Client.AnswerCallbackQueryAsync(q.Id, null, false, null, 0);
                                Client.EditMessageCaptionAsync(q.Message.Chat.Id, q.Message.MessageId, $"{q.Message.Caption}\n\nPlease choose why it is disapproved:",
                                    menu);
                            }
                        }

                        break;
                    case "dis":
                        using (var db = new tdthemeEntities())
                        {
                            var th = db.Themes.Find(int.Parse(args[1]));
                            string msg = "Your theme was not approved\n";
                            var send = true;
                            switch (args[2])
                            {
                                case "img":
                                    msg += "Please use the official Telegram theme preview screenshot\n";
                                    break;
                                case "text":
                                    msg += "Please update the Name / Description with something more descriptive / meaningful\n";
                                    break;
                                case "imgtext":
                                    msg += "Please use the official Telegram theme preview screenshot\n";
                                    msg += "Please update the Name / Description with something more descriptive / meaningful\n";
                                    break;
                                case "other":
                                    Client.AnswerCallbackQueryAsync(q.Id, null, false, null, 0);
                                    Client.EditMessageCaptionAsync(q.Message.Chat.Id, q.Message.MessageId, $"{q.Message.Caption}\n\nPlease use the manual disapproval command",
                                        null);
                                    send = false;
                                    break;
                            }

                            if (send)
                            {
                                th.Approved = false;
                                db.SaveChanges();
                                msg += "Use /edittheme to update your submission";
                                Client.SendTextMessageAsync(th.User.TelegramID, msg);
                                Client.AnswerCallbackQueryAsync(q.Id, null, false, null, 0);
                                Client.EditMessageCaptionAsync(q.Message.Chat.Id, q.Message.MessageId, $"{q.Message.Caption}\n\nMessage sent to user",
                                        null);
                            }
                        }

                        break;
                    case "update":
                        using (var db = new tdthemeEntities())
                        {
                            t = db.Themes.Find(int.Parse(args[1]));
                            lu.ThemeUpdating = t;
                            //do newtheme tasks
                            lu.QuestionAsked = QuestionType.ThemeName;
                            Client.AnswerCallbackQueryAsync(q.Id, null, false, null, 0);
                            Client.EditMessageTextAsync(q.From.Id, q.Message.MessageId,
                                "Updating theme.  Please enter a new name, or hit /keep to keep the same name:\n" +
                                t.Name);
                        }
                        break;
                    case "delete":
                        using (var db = new tdthemeEntities())
                        {
                            t = db.Themes.Find(int.Parse(args[1]));
                            Client.AnswerCallbackQueryAsync(q.Id, null, false, null, 0);
                            Client.EditMessageTextAsync(q.From.Id, q.Message.MessageId,
                                $"Are you sure you want to delete {t.Name}?", replyMarkup:
                                    new InlineKeyboardMarkup(new[]
                                    {
                                        new InlineKeyboardButton("Yes", $"confirm|{args[1]}"),
                                        new InlineKeyboardButton("Cancel", "confirm|no")
                                    }));
                        }
                        break;
                    case "confirm":
                        if (args[1] != "no")
                        {
                            using (var db = new tdthemeEntities())
                            {
                                t = db.Themes.Find(int.Parse(args[1]));
                                foreach (var r in t.Ratings)
                                    db.Ratings.Remove(r);
                                foreach (var d in t.Downloads)
                                    db.Downloads.Remove(d);
                                db.Themes.Remove(t);
                                db.SaveChanges();
                                Client.AnswerCallbackQueryAsync(q.Id, null, false, null, 0);
                                Client.EditMessageTextAsync(q.From.Id, q.Message.MessageId,
                                    $"{t.Name} has been deleted.");
                            }
                        }
                        else
                        {
                            Client.AnswerCallbackQueryAsync(q.Id, null, false, null, 0);
                            Client.EditMessageTextAsync(q.From.Id, q.Message.MessageId, "Cancelled delete operation");
                        }
                        break;
                    case "rate":
                        Client.AnswerCallbackQueryAsync(q.Id, null, false, null, 0);
                        if (args[1] == "no")
                        {
#if !DEBUG
                            Client.EditMessageCaptionAsync(q.From.Id, q.Message.MessageId, "Enjoy your theme!");
#else
                            Client.EditMessageTextAsync(q.From.Id, q.Message.MessageId, "Enjoy your theme!");
#endif
                        }
                        else
                        {
                            using (var db = new tdthemeEntities())
                            {
                                var themeId = args[1];
                                var rTheme = db.Themes.FirstOrDefault(x => x.Id.ToString() == themeId);
                                var rUser = db.Users.FirstOrDefault(x => x.TelegramID == q.From.Id);
                                var rating = db.Ratings.FirstOrDefault(x => x.UserId == rUser.Id && x.ThemeId == rTheme.Id);
                                if (rating == null)
                                {
                                    rating = new Rating
                                    {
                                        ThemeId = rTheme.Id,
                                        UserId = rUser.Id,
                                        TimeStamp = DateTime.UtcNow,
                                        Rating1 = int.Parse(args[2])
                                    };
                                    db.Ratings.Add(rating);
                                }
                                else
                                    rating.Rating1 = int.Parse(args[2]);
                                db.SaveChanges();
                            }
#if !DEBUG
                            Client.EditMessageCaptionAsync(q.From.Id, q.Message.MessageId, "Thank you for rating!");
#else
                            Client.EditMessageTextAsync(q.From.Id, q.Message.MessageId, "Thank you for rating!");
#endif
                        }
                        break;

                }


            }
            catch (AggregateException e)
            {
                Client.SendTextMessageAsync(ThemeGroup, e.InnerExceptions[0].Message);
            }
            catch (Exception e)
            {
                while (e.InnerException != null)
                    e = e.InnerException;
                Client.SendTextMessageAsync(ThemeGroup, e.Message + "\n" + e.StackTrace);
            }
        }

        private static void SaveTheme(LUser lu, bool update = false)
        {
            try
            {
                using (var db = new tdthemeEntities())
                {
                    if (!update)
                    {
                        //get the database user
                        var thisUser = db.Users.FirstOrDefault(x => x.TelegramID == lu.Id);
                        if (thisUser.AccessFlags == null)
                            thisUser.AccessFlags = 0;
                        var flags = (Access)thisUser.AccessFlags;
                        if (flags.HasFlag(Access.AutoApprove))
                            lu.ThemeCreating.Approved = true;
                        else
                            lu.ThemeCreating.Approved = null;
                        lu.ThemeCreating.LastUpdated = DateTime.UtcNow;
                        thisUser.Themes.Add(lu.ThemeCreating);
                        db.SaveChanges();

                        if (flags.HasFlag(Access.AutoApprove))
                        {
                            Client.SendTextMessageAsync(lu.Id, "Your theme is ready!");
                            CacheThemes();
                        }
                        else
                        {
                            //theme is awaiting approval, PM Para
                            RequestApproval(lu.ThemeCreating.Id);

                            Client.SendTextMessageAsync(lu.Id,
                                "Your theme has been uploaded, and is awaiting approval from a moderator");
                        }
                    }
                    else
                    {
                        var t = db.Themes.FirstOrDefault(x => x.Id == lu.ThemeUpdating.Id);
                        var send = t.Approved != true;
                        t.Approved = t.Approved == false ? null : t.Approved;
                        var th = lu.ThemeUpdating;
                        t.Description = th.Description;
                        t.FileName = th.FileName;
                        t.File_Id = th.File_Id;
                        t.Name = th.Name;
                        t.Photo_Id = th.Photo_Id;
                        t.ShowOwnerName = th.ShowOwnerName;
                        t.ShowOwnerUsername = th.ShowOwnerUsername;
                        t.LastUpdated = DateTime.UtcNow;
                        db.SaveChanges();
                        if (send)
                        {
                            Client.SendTextMessageAsync(lu.Id, "Your theme is pending approval.");
                            RequestApproval(t.Id);
                        }
                        else
                        {
                            CacheThemes();
                            Client.SendTextMessageAsync(lu.Id, "Your theme is ready!");
                        }

                    }


                    LocalUsers.Remove(lu);
                    lu = null;
                }
            }
            catch (AggregateException e)
            {
                Client.SendTextMessageAsync(129046388, e.InnerExceptions[0].Message);
            }
            catch (Exception e)
            {
                while (e.InnerException != null)
                    e = e.InnerException;
                Client.SendTextMessageAsync(129046388, e.Message + "\n" + e.StackTrace);
            }
        }

        private static void RequestApproval(int id)
        {
            using (var db = new tdthemeEntities())
            {
                var t = db.Themes.FirstOrDefault(x => x.Id == id);
                //create menu
                var menu = new InlineKeyboardMarkup(new[]
                {
                    new[]
                    {
                        new InlineKeyboardButton("Approve", $"app|{id}|yes"),
                        new InlineKeyboardButton("Disapprove", $"app|{id}|no"),
                    }
                });
                Client.SendPhotoAsync(ThemeGroup, t.Photo_Id,
                                    $"Theme pending approval:\n\n{t.Id}\n{t.Name}\n{t.Description}\n{(t.User.Username == null ? t.User.Name : "@" + t.User.Username)}", replyMarkup: menu);
                Client.SendDocumentAsync(ThemeGroup, t.File_Id);
            }


        }

        private static async void ClientOnOnMessage(object sender, MessageEventArgs messageEventArgs)
        {
            new Thread(() => HandleMessage(messageEventArgs.Message)).Start();
        }

        private static void HandleMessage(Message m)
        {
            try
            {
                if (m.Date < DateTime.UtcNow.AddSeconds(-30)) return; //ignore old / lagged messages
                if (m.Type == MessageType.TextMessage)
                {
                    CreateOrUpdateDBUser(m.From);
                    if (m.Text.StartsWith("/") || m.Text.StartsWith("!"))
                    {
                        LUser lu;
                        var param = m.Text.Split(' ');
                        var cmd = param[0].Replace("/", "").Replace("!", "");

                        switch (cmd)
                        {
                            case "start":
                                if (param.Length > 1)
                                {
                                    //check for start parameter
                                    var arg = param[1];
                                    if (arg.StartsWith("t")) //get the theme
                                    {
                                        //they want a theme
                                        arg = arg.Substring(1);
                                        using (var db = new tdthemeEntities())
                                        {
                                            var theme = db.Themes.FirstOrDefault(x => x.Id.ToString() == arg);
                                            var dUser = db.Users.FirstOrDefault(x => x.TelegramID == m.From.Id);
                                            if (theme != null && dUser != null)
                                            {
                                                theme.Downloads.Add(new Download
                                                {
                                                    TimeStamp = DateTime.UtcNow,
                                                    UserID = dUser.Id
                                                });
                                                db.SaveChanges();
                                            }
                                            Client.SendDocumentAsync(m.Chat.Id, new FileToSend(theme.File_Id),
                                                theme.Name);

                                        }
                                        break;
                                    }
                                    if (arg.StartsWith("r")) //rate the theme
                                    {
                                        arg = arg.Substring(1);
                                        using (var db = new tdthemeEntities())
                                        {
                                            var theme = db.Themes.FirstOrDefault(x => x.Id.ToString() == arg);
                                            //send rate menu
#if !DEBUG
                                            var result =
                                                Client.SendPhotoAsync(m.From.Id, theme.Photo_Id,
                                                    $"How would you rate {theme.Name}?\n(1 - Did not like at all, 5 - it's awesome!)",
                                                    replyMarkup:
                                                    new InlineKeyboardMarkup(new[]
                                                    {
                                                        Enumerable.Range(1, 5)
                                                            .Select(
                                                                x =>
                                                                    new InlineKeyboardButton(x.ToString(),
                                                                        $"rate|{theme.Id}|{x}"))
                                                            .ToArray(),
                                                        new[]
                                                        {
                                                            new InlineKeyboardButton("No Thanks", "rate|no")
                                                        },
                                                    })).Result;
#else
                                            var result = Client.SendTextMessageAsync(m.From.Id, $"How would you rate {theme.Name}?\n(1 - Did not like at all, 5 - it's awesome!)", replyMarkup:
                                                    new InlineKeyboardMarkup(new[] {
                                                Enumerable.Range(1, 5).Select(x => new InlineKeyboardButton(x.ToString(), $"rate|{theme.Id}|{x}")).ToArray(),
                                                new []
                                            {
                                                new InlineKeyboardButton("No Thanks", "rate|no")
                                            }, })).Result;
#endif
                                        }
                                        break;
                                    }
                                }
                                if (m.Chat.Type == ChatType.Private)
                                {


                                    Client.SendTextMessageAsync(m.From.Id,
                                        "Welcome to Telegram Themes Search Bot!\nHere you can search for cool themes for Telegram, or upload your own creations for others to use.\n\nAvailable commands:\n" +
                                        "/help - Show this list\n" +
                                        "/newtheme - Upload a new theme to the catalog\n" +
                                        "/edittheme - Edit one of your themes\n" +
                                        "/deletetheme - Delete a theme\n" +
                                        "/cancel - Cancel the current operation\n\n" +
                                        "You can also search for themes inline");
                                }
                                break;
                            case "help":

                                Client.SendTextMessageAsync(m.Chat.Id, "Available commands:\n" +
                                                                       "/help - Show this list\n" +
                                                                       "/newtheme - Upload a new theme to the catalog\n" +
                                                                       "/edittheme - Edit one of your themes\n" +
                                                                       "/deletetheme - Delete a theme\n" +
                                                                       "/cancel - Cancel the current operation\n\n" +
                                                                       "You can also search for themes inline");
                                break;
                            case "newtheme":
                                if (m.Chat.Type == ChatType.Private)
                                {
                                    lu = GetLocalUser(m.From.Id);
                                    Client.SendTextMessageAsync(m.From.Id,
                                        "Great! You want to upload a new theme.  Let's begin.\nFirst, what is the name of your theme?");
                                    lu.ThemeCreating = new Theme();
                                    lu.QuestionAsked = QuestionType.ThemeName;
                                }
                                break;
                            case "edittheme":
                                lu = GetLocalUser(m.From.Id);
                                LocalUsers.Remove(lu);
                                lu = null;
                                if (m.Chat.Type != ChatType.Private)
                                {
                                    Client.SendTextMessageAsync(m.Chat.Id, "Please edit your themes in PM");
                                    break;
                                }
                                using (var db = new tdthemeEntities())
                                {
                                    var usr = db.Users.FirstOrDefault(x => x.TelegramID == m.From.Id);
                                    if (usr != null)
                                    {
                                        if (usr.Themes.Any())
                                        {
                                            Client.SendTextMessageAsync(m.From.Id,
                                                "Which theme do you want to update?",
                                                replyMarkup: new InlineKeyboardMarkup(
                                                    usr.Themes.Select(
                                                            x => new[] { new InlineKeyboardButton(x.Name, $"update|{x.Id}") })
                                                        .ToArray()
                                                ));
                                            break;
                                        }
                                    }
                                    Client.SendTextMessageAsync(m.From.Id,
                                        "You don't have any themes to update!  Upload new themes with /newtheme");
                                }
                                break;
                            case "deletetheme":
                                if (m.Chat.Type != ChatType.Private)
                                {
                                    Client.SendTextMessageAsync(m.Chat.Id, "Please delete your themes in PM");
                                    break;
                                }
                                using (var db = new tdthemeEntities())
                                {
                                    var usr = db.Users.FirstOrDefault(x => x.TelegramID == m.From.Id);
                                    if (usr != null)
                                    {
                                        if (usr.Themes.Any())
                                        {
                                            Client.SendTextMessageAsync(m.From.Id,
                                                "Which theme do you want to delete?",
                                                replyMarkup: new InlineKeyboardMarkup(
                                                    usr.Themes.Select(
                                                            x => new[] { new InlineKeyboardButton(x.Name, $"delete|{x.Id}") })
                                                        .ToArray()
                                                ));
                                            break;
                                        }
                                    }
                                    Client.SendTextMessageAsync(m.From.Id,
                                        "You don't have any themes to update!  Upload new themes with /newtheme");
                                }
                                break;
                            case "cancel":
                                if (m.Chat.Type == ChatType.Private)
                                {
                                    lu = GetLocalUser(m.From.Id);
                                    LocalUsers.Remove(lu);
                                    lu = null;
                                    Client.SendTextMessageAsync(m.From.Id, "Operation cancelled");
                                }
                                
                                break;
                            case "keep":
                                lu = GetLocalUser(m.From.Id);
                                if (lu.ThemeUpdating == null)
                                {
                                    //ignore
                                }
                                else
                                {
                                    string msg = "";
                                    InlineKeyboardMarkup menu = null;
                                    switch (lu.QuestionAsked)
                                    {
                                        case QuestionType.ThemeName:
                                            msg =
                                                "Ok, keeping the name.  What about the description? Again, press /keep to keep the same:\n" +
                                                lu.ThemeUpdating.Description;
                                            lu.QuestionAsked = QuestionType.Description;
                                            break;
                                        case QuestionType.FileUpload:
                                            msg = "No file change.  What about the screenshot? /keep to not change it.";
                                            lu.QuestionAsked = QuestionType.Image;
                                            break;
                                        case QuestionType.Description:
                                            msg =
                                                "Ok, keep the description.  How about the file.  /keep to keep the same file, or upload a new one to me.";
                                            lu.QuestionAsked = QuestionType.FileUpload;
                                            break;
                                        case QuestionType.Image:
                                            msg = "Got it, keep the image.  Do you want your name shown on the listing?";
                                            menu =
                                                new InlineKeyboardMarkup(new[]
                                                {
                                                    new InlineKeyboardButton("Yes", "showname|yes"),
                                                    new InlineKeyboardButton("No", "showname|no")
                                                });
                                            lu.QuestionAsked = QuestionType.ShowOwnerName;
                                            break;
                                        case QuestionType.None:
                                            break;
                                        default:
                                            break;
                                    }

                                    if (msg != "")
                                    {
                                        Client.SendTextMessageAsync(m.From.Id, msg, replyMarkup: menu);
                                    }
                                }
                                break;
                            case "approval":
                                try
                                {
                                    if (m.From.Id == 129046388 || IsModerator(m.From))
                                    {
                                        using (var db = new tdthemeEntities())
                                        {
                                            var toApprove = db.Themes.Where(x => x.Approved == null).Take(5).ToList();
                                            foreach (var t in toApprove)
                                            {
                                                RequestApproval(t.Id);
                                                Thread.Sleep(1000);
                                            }
                                        }
                                    }
                                }
                                catch (AggregateException e)
                                {
                                    Client.SendTextMessageAsync(m.From.Id, e.InnerExceptions[0].Message);
                                }
                                catch (Exception e)
                                {
                                    while (e.InnerException != null)
                                        e = e.InnerException;
                                    Client.SendTextMessageAsync(m.From.Id, e.Message + "\n" + e.StackTrace);
                                }
                                break;
                            case "approve":
                                try
                                {
                                    if (m.From.Id == 129046388 || IsModerator(m.From))
                                    {
                                        var toApprove = int.Parse(m.Text.Split(' ')[1]);
                                        using (var db = new tdthemeEntities())
                                        {
                                            var t = db.Themes.FirstOrDefault(x => x.Id == toApprove);
                                            t.Approved = true;
                                            db.SaveChanges();
                                            CacheThemes();
                                            Client.SendTextMessageAsync(t.User.TelegramID,
                                                $"Your theme {t.Name} has been approved and is now listed!");
                                        }
                                    }
                                }
                                catch (AggregateException e)
                                {
                                    Client.SendTextMessageAsync(m.From.Id, e.InnerExceptions[0].Message);
                                }
                                catch (Exception e)
                                {
                                    while (e.InnerException != null)
                                        e = e.InnerException;
                                    Client.SendTextMessageAsync(m.From.Id, e.Message + "\n" + e.StackTrace);
                                }
                                break;
                            case "disapprove":
                                try
                                {
                                    if (m.From.Id == 129046388 || IsModerator(m.From))
                                    {
                                        var toApprove = int.Parse(m.Text.Split(' ')[1]);
                                        using (var db = new tdthemeEntities())
                                        {
                                            var t = db.Themes.FirstOrDefault(x => x.Id == toApprove);
                                            t.Approved = false;
                                            db.SaveChanges();
                                            var id = t.User.TelegramID;
                                            Client.SendTextMessageAsync(id,
                                                $"Your theme {t.Name} was not approved.\n\n{m.Text.Replace("/disapprove " + toApprove, "")}");
                                        }
                                    }
                                }
                                catch (AggregateException e)
                                {
                                    Client.SendTextMessageAsync(m.From.Id, e.InnerExceptions[0].Message);
                                }
                                catch (Exception e)
                                {
                                    while (e.InnerException != null)
                                        e = e.InnerException;
                                    Client.SendTextMessageAsync(m.From.Id, e.Message + "\n" + e.StackTrace);
                                }
                                break;
                            case "delete":
                                try
                                {
                                    if (m.From.Id == 129046388 || IsModerator(m.From))
                                    {
                                        var toApprove = int.Parse(m.Text.Split(' ')[1]);
                                        using (var db = new tdthemeEntities())
                                        {
                                            var t = db.Themes.FirstOrDefault(x => x.Id == toApprove);
                                            db.Themes.Remove(t);
                                            db.SaveChanges();
                                        }
                                    }
                                }
                                catch (AggregateException e)
                                {
                                    Client.SendTextMessageAsync(m.From.Id, e.InnerExceptions[0].Message);
                                }
                                catch (Exception e)
                                {
                                    while (e.InnerException != null)
                                        e = e.InnerException;
                                    Client.SendTextMessageAsync(m.From.Id, e.Message + "\n" + e.StackTrace);
                                }
                                break;
                            case "setmod":
                                try
                                {
                                    if (m.From.Id == 129046388)
                                    {
                                        var toApprove = int.Parse(m.Text.Split(' ')[1]);
                                        using (var db = new tdthemeEntities())
                                        {
                                            var u = db.Users.FirstOrDefault(x => x.TelegramID == toApprove);
                                            if (u.AccessFlags == null)
                                                u.AccessFlags = 0;
                                            var flags = (Access)u.AccessFlags;
                                            if (flags.HasFlag(Access.Moderator)) return;
                                            flags = flags | Access.Moderator;
                                            u.AccessFlags = (int)flags;
                                            db.SaveChanges();
                                            Client.SendTextMessageAsync(toApprove,
                                                "You now have moderator access.\n/approval - get 5 themes awaiting approval\n/disapprove <id> <reason> - disapprove theme, send reason to creator\n/approve <id> - approve theme for publication\n/delete <id> - Delete junk submission");
                                            Client.SendTextMessageAsync(m.From.Id,
                                                u.Name + " has been set to moderator");
                                        }
                                    }
                                }
                                catch
                                {
                                    // ignored
                                }
                                break;
                            case "setapproved":
                                try
                                {
                                    if (m.From.Id == 129046388)
                                    {
                                        var toApprove = int.Parse(m.Text.Split(' ')[1]);
                                        using (var db = new tdthemeEntities())
                                        {
                                            var u = db.Users.FirstOrDefault(x => x.TelegramID == toApprove);
                                            if (u.AccessFlags == null)
                                                u.AccessFlags = 0;
                                            var flags = (Access)u.AccessFlags;
                                            if (flags.HasFlag(Access.AutoApprove)) return;
                                            flags = flags | Access.AutoApprove;
                                            u.AccessFlags = (int)flags;
                                            db.SaveChanges();
                                            Client.SendTextMessageAsync(toApprove,
                                                "You now have auto approval access.  New themes you submit will not require a moderator approval, and will be added instantly.");
                                            Client.SendTextMessageAsync(m.From.Id,
                                                u.Name + " has been set to auto approve");
                                        }
                                    }
                                }
                                catch
                                {
                                    // ignored
                                }
                                break;
                            case "download":
                                if (m.From.Id != 129046388) return;
                                DownloadThemes();
                                break;
                            case "cachethemes":
                                if (m.From.Id != 129046388) return;
                                CacheThemes();
                                break;
                            case "checkpending":
                                if (m.From.Id != 129046388) return;
                                CheckPending();
                                break;
                        }
                    }
                    else if (m.Chat.Type == ChatType.Private)
                    {
                        //plain text.  Are they answering a question, or searching?
                        var lu = GetLocalUser(m.From.Id);
                        switch (lu.QuestionAsked)
                        {
                            case QuestionType.ThemeName:
                                if (m.Text.Length >= 3)
                                {
                                    if (lu.ThemeCreating != null)
                                        lu.ThemeCreating.Name = m.Text;
                                    else
                                        lu.ThemeUpdating.Name = m.Text;
                                    lu.QuestionAsked = QuestionType.Description;
                                    Client.SendTextMessageAsync(lu.Id,
                                        "Alright, now enter a short description of your theme.  It helps to use keywords like \"dark\", \"blue\", etc.  This will help people locate your theme easier.\nAlso, if your theme is a modification of another theme, please add \"Based on <theme name>\"");
                                }
                                else
                                {
                                    Client.SendTextMessageAsync(m.From.Id,
                                        "Please enter a name for your theme.  It needs to be at least 3 characters.");
                                }
                                break;
                            case QuestionType.FileUpload:
                                Client.SendTextMessageAsync(m.From.Id, "Please upload a file");
                                break;
                            case QuestionType.Description:
                                if (m.Text.Length >= 5)
                                {
                                    if (lu.ThemeCreating != null)
                                        lu.ThemeCreating.Description = m.Text;
                                    else
                                        lu.ThemeUpdating.Description = m.Text;
                                    lu.QuestionAsked = QuestionType.FileUpload;
                                    Client.SendTextMessageAsync(lu.Id, "Great, now upload the file to me.\nNotes: If using *nix, please use 7zip, or be careful about what compression method is used. It also helps to create the zip file with .zip, then rename to .tdesktop-theme - not create it AS tdesktop-theme\nMake sure your color file is named colors.tdesktop-theme, and the image is named 'background.jpg', 'background.png', 'tiled.jpg' or 'tiled.png'.");
                                }
                                else
                                {
                                    Client.SendTextMessageAsync(m.From.Id,
                                        "Please enter a description.  It needs to be at least 5 characters");
                                }
                                break;
                            case QuestionType.ShowOwnerName:
                                break;
                            case QuestionType.ShowOwnerUsername:
                                break;
                            case QuestionType.Image:
                                break;
                            case QuestionType.None:
                                break;
                            default:
                                throw new ArgumentOutOfRangeException();
                        }
                    }
                }
                else if (m.Type == MessageType.DocumentMessage)
                {
                    var lu = GetLocalUser(m.From.Id);
                    switch (lu.QuestionAsked)
                    {
                        case QuestionType.FileUpload:

                            var filename = m.Document.FileName;
                            if (!filename.ToLower().EndsWith("tdesktop-theme"))
                            {
                                Client.SendTextMessageAsync(lu.Id, "Your file needs to be of type `.tdesktop-theme`",
                                    parseMode: ParseMode.Markdown);
                                return;
                            }
                            var dir = Path.Combine(ThemesDirectory, "..\\Temp\\" + lu.Id);
                            Directory.CreateDirectory(dir);
                            var path = Path.Combine(dir, filename);
                            //download and check the file against the cache
                            var fs = new FileStream(path, FileMode.Create);
                            var file = Client.GetFileAsync(m.Document.FileId, fs).Result;
                            fs.Close();
                            var unzipPath = Path.Combine(dir, "unzip");
                            Directory.CreateDirectory(unzipPath);
                            File.Delete(path.Replace(".tdesktop-theme", ".zip"));
                            File.Move(path, path.Replace(".tdesktop-theme", ".zip"));
                            path = path.Replace(".tdesktop-theme", ".zip");

                            using (var zip = ZipFile.Read(path))
                            {
                                foreach (var f in zip)
                                {
                                    f.Extract(unzipPath, ExtractExistingFileAction.OverwriteSilently);
                                }
                            }

                            var themeFiles = Directory.GetFiles(unzipPath, "*.tdesktop-theme");
                            if (themeFiles.Length > 1)
                            {
                                Client.SendTextMessageAsync(lu.Id,
                                    "Your theme file contains more than one tdesktop-theme inside it.  Please have only one of these files.");
                                break;
                            }

                            if (themeFiles.Length != 1)
                            {
                                Client.SendTextMessageAsync(lu.Id, "Your theme file contains no theme inside it.");
                                break;
                            }
                            var theme = themeFiles[0];
                            //now read the theme file

                            var dict = new Dictionary<string, string>();
                            //read the file
                            var line = "";
                            using (var sr = new StreamReader(theme))
                            {
                                while ((line = sr.ReadLine()) != null)
                                {
                                    if (String.IsNullOrEmpty(line) || line.StartsWith("//")) continue;
                                    try
                                    {
                                        var lineSplit = line.Split(':');
                                        dict.Add(lineSplit[0].Trim(), lineSplit[1].Split(';')[0].Trim());
                                    }
                                    catch
                                    {

                                    }
                                }
                            }

                            //compare to cache
                            var same = false;
                            foreach (var d in CachedThemes)
                            {
                                var count = 0;
                                foreach (var value in dict)
                                {
                                    if (d.ContainsKey(value.Key))
                                    {
                                        if (d[value.Key] == value.Value)
                                        {
                                            count++;
                                        }
                                    }
                                    else
                                    {
                                        count++;
                                    }

                                }

                                if (count >= dict.Count)
                                {
                                    same = true;
                                }

                            }

                            if (same)
                            {
                                Client.SendTextMessageAsync(lu.Id,
                                    "This theme matches an existing theme.  Simply changing the background, or re-uploading a theme does not count as creating a new theme.");
                                break;
                            }




                            if (lu.ThemeCreating != null)
                            {
                                lu.ThemeCreating.FileName = filename;
                                lu.ThemeCreating.File_Id = m.Document.FileId;
                            }
                            else
                            {
                                lu.ThemeUpdating.FileName = filename;
                                lu.ThemeUpdating.File_Id = m.Document.FileId;

                            }
                            Client.SendTextMessageAsync(lu.Id,
                                "Awesome.  Now, upload a screenshot that I can show to users when they search for your theme");
                            lu.QuestionAsked = QuestionType.Image;
                            //fs.Dispose();
                            //File.Delete(filename);


                            break;
                        case QuestionType.Image:
                            Client.SendTextMessageAsync(lu.Id, "Please send the photo as compressed, not as a document");
                            break;
                        default:
                            //didn't ask for a file....
                            break;
                    }
                }
                else if (m.Type == MessageType.PhotoMessage)
                {
                    var lu = GetLocalUser(m.From.Id);
                    switch (lu.QuestionAsked)
                    {
                        case QuestionType.Image:
                            if (lu.ThemeCreating != null)
                                lu.ThemeCreating.Photo_Id = m.Photo[0].FileId;
                            else
                                lu.ThemeUpdating.Photo_Id = m.Photo[0].FileId;
                            lu.QuestionAsked = QuestionType.ShowOwnerName;
                            Client.SendTextMessageAsync(lu.Id,
                                "Got it.  Now a couple simple questions.\nFirst, do you want your name to be shown in the search result for your theme?",
                                replyMarkup:
                                new InlineKeyboardMarkup(new[]
                                {
                                    new InlineKeyboardButton("Yes", "showname|yes"),
                                    new InlineKeyboardButton("No", "showname|no")
                                }));

                            break;
                        default:
                            //didn't ask for a photo...
                            break;
                    }
                }
            }
            catch (AggregateException e)
            {
                Client.SendTextMessageAsync(ThemeGroup, e.InnerExceptions[0].Message);
                Client.ForwardMessageAsync(ThemeGroup, m.Chat.Id, m.MessageId);
            }
            catch (Exception e)
            {
                while (e.InnerException != null)
                    e = e.InnerException;
                if (e.Message.Trim().StartsWith("Bad sig"))
                {
                    Client.SendTextMessageAsync(m.From.Id,
                        $"It looks like you are using *nix.  Please try zipping the file with 7zip or something else.\n{e.Message}");
                }
                else
                {
                    Client.SendTextMessageAsync(m.From.Id, e.Message);
                    Client.SendTextMessageAsync(ThemeGroup, e.Message + "\n" + e.StackTrace);
                    Client.ForwardMessageAsync(ThemeGroup, m.Chat.Id, m.MessageId);
                }

            }
        }

        private static bool IsModerator(Telegram.Bot.Types.User u)
        {
            using (var db = new tdthemeEntities())
            {
                var user = db.Users.FirstOrDefault(x => x.TelegramID == u.Id);
                if (user.AccessFlags == null) return false;
                var flags = (Access)user.AccessFlags;
                return flags.HasFlag(Access.Moderator);
            }
        }

        private static void CheckPending()
        {
            try
            {
                using (var db = new tdthemeEntities())
                {
                    var pending = db.Themes.Where(x => x.Approved == null);
                    foreach (var pend in pending)
                    {
                        //download the theme
                        Console.WriteLine("Checking: " + pend.Name);
                        var dir = Path.Combine(ThemesDirectory, "..\\Temp\\" + pend.Id);
                        Directory.CreateDirectory(dir);
                        var path = Path.Combine(dir, pend.FileName);
                        //download and check the file against the cache
                        var fs = new FileStream(path, FileMode.Create);
                        var file = Client.GetFileAsync(pend.File_Id, fs).Result;
                        fs.Close();
                        var unzipPath = Path.Combine(dir, "unzip");
                        Directory.CreateDirectory(unzipPath);
                        File.Delete(path.Replace(".tdesktop-theme", ".zip"));
                        File.Move(path, path.Replace(".tdesktop-theme", ".zip"));
                        path = path.Replace(".tdesktop-theme", ".zip");
                        var id = pend.User.Id;
                        try
                        {
                            using (var zip = ZipFile.Read(path))
                            {
                                foreach (var f in zip)
                                {
                                    f.Extract(unzipPath, ExtractExistingFileAction.OverwriteSilently);
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine(e.Message);
                            Client.SendTextMessageAsync(id, "Your theme file was unable to be unpacked: " + e.Message + "\n please repack and resubmit");
                            pend.Approved = false;

                            continue;
                        }

                        var themeFiles = Directory.GetFiles(unzipPath, "*.tdesktop-theme");
                        if (themeFiles.Length > 1)
                        {
                            Console.WriteLine("Multiple color files");
                            Client.SendTextMessageAsync(id, "Your theme file contains more than one tdesktop-theme inside it.  Please have only one of these files.");
                            pend.Approved = false;

                            continue;
                        }

                        if (themeFiles.Length != 1)
                        {
                            Console.WriteLine("No color files");
                            Client.SendTextMessageAsync(id, "Your theme file contains no theme inside it.");
                            pend.Approved = false;

                            continue;
                        }
                        var theme = themeFiles[0];
                        //now read the theme file

                        var dict = new Dictionary<string, string>();
                        //read the file
                        var line = "";
                        using (var sr = new StreamReader(theme))
                        {
                            while ((line = sr.ReadLine()) != null)
                            {
                                if (String.IsNullOrEmpty(line) || line.StartsWith("//")) continue;
                                try
                                {
                                    var lineSplit = line.Split(':');
                                    dict.Add(lineSplit[0].Trim(), lineSplit[1].Split(';')[0].Trim());
                                }
                                catch
                                {

                                }
                            }
                        }

                        //compare to cache
                        var same = false;
                        foreach (var d in CachedThemes)
                        {
                            var count = 0;
                            foreach (var value in dict)
                            {
                                if (d.ContainsKey(value.Key))
                                {
                                    if (d[value.Key] == value.Value)
                                    {
                                        count++;
                                    }
                                }
                                else
                                {
                                    count++;
                                }

                            }

                            if (count >= dict.Count)
                            {
                                same = true;
                            }

                        }

                        if (same)
                        {
                            Console.WriteLine("Matches existing");
                            pend.Approved = false;
                            Client.SendTextMessageAsync(id,
                                "This theme matches an existing theme.  Simply changing the background, or re-uploading a theme does not count as creating a new theme.");

                        }

                    }

                    db.SaveChanges();
                }
            }
            catch (AggregateException e)
            {
                Client.SendTextMessageAsync(129046388, e.InnerExceptions[0].Message);
            }
            catch (Exception e)
            {
                while (e.InnerException != null)
                    e = e.InnerException;
                Client.SendTextMessageAsync(129046388, e.Message + "\n" + e.StackTrace);
            }
        }

        private static void DownloadThemes()
        {
            try
            {
                Directory.CreateDirectory(ThemesDirectory);
                var temp = new List<Theme>();
                //initialize the folder with all the themes
                using (var db = new tdthemeEntities())
                {
                    foreach (var t in db.Themes.Where(x => x.Approved == true).Include(x => x.User).ToList())
                    {
                        temp.Add(t);
                        if (Directory.Exists(Path.Combine(ThemesDirectory, t.Id.ToString()))) continue;
                        Directory.CreateDirectory(Path.Combine(ThemesDirectory, t.Id.ToString()));
                        var path = Path.Combine(ThemesDirectory, t.Id + "\\" + t.FileName);
                        using (var fs = new FileStream(path, FileMode.Create))
                        {
                            var res = Client.GetFileAsync(t.File_Id, fs).Result;
                            //wait for the file to download
                            fs.Close();
                        }
                        Thread.Sleep(500);
                    }
                }
                LoadedThemes.Clear();
                LoadedThemes.AddRange(temp);
            }
            catch (AggregateException e)
            {
                Client.SendTextMessageAsync(129046388, e.InnerExceptions[0].Message);
            }
            catch (Exception e)
            {
                while (e.InnerException != null)
                    e = e.InnerException;
                Client.SendTextMessageAsync(129046388, e.Message + "\n" + e.StackTrace);
            }
        }

        private static void CacheThemes()
        {
#if !DEBUG
            DownloadThemes();
#endif
            try
            {
                var temp = new List<Dictionary<string, string>>();
                foreach (var d in Directory.GetDirectories(ThemesDirectory))
                {
                    Console.WriteLine("Caching: " + d.Split('\\').Last());
                    var path = Directory.GetFiles(d, "*.tdesktop-theme").FirstOrDefault();
                    if (path == null)
                        path = Directory.GetFiles(d, "*.zip").FirstOrDefault();
                    if (path != null)
                    {
                        if (!path.EndsWith("zip"))
                            if (File.Exists(path))
                                File.Move(path, path.Replace(".tdesktop-theme", ".zip"));
                        path = path.Replace(".tdesktop-theme", ".zip");
                        var unzipPath = Path.Combine(d, "unzip");
                        Directory.CreateDirectory(unzipPath);
                        try
                        {
                            //unzip
                            using (var zip = ZipFile.Read(path))
                            {
                                foreach (ZipEntry f in zip)
                                {
                                    f.Extract(unzipPath, ExtractExistingFileAction.OverwriteSilently);
                                }
                            }
                        }
                        catch
                        {
                            //bad zip?
                            File.Delete(path);
                            Directory.Delete(unzipPath, true);
                            Directory.Delete(d, true);
                            continue;
                        }

                        var themeFile =
                            Directory.GetFiles(unzipPath, "*.tdesktop-theme").FirstOrDefault();
                        if (themeFile != null)
                        {
                            var dict = new Dictionary<string, string>();
                            //read the file
                            var line = "";
                            using (var sr = new StreamReader(themeFile))
                            {
                                while ((line = sr.ReadLine()) != null)
                                {
                                    if (String.IsNullOrEmpty(line) || line.StartsWith("//")) continue;
                                    try
                                    {
                                        var lineSplit = line.Split(':');
                                        dict.Add(lineSplit[0].Trim(), lineSplit[1].Split(';')[0].Trim());
                                    }
                                    catch
                                    {

                                    }
                                }
                            }
                            if (dict.Any())
                                temp.Add(dict);
                        }
                    }
                }
                CachedThemes.Clear();
                CachedThemes.AddRange(temp);
            }
            catch (AggregateException e)
            {
                Client.SendTextMessageAsync(129046388, e.InnerExceptions[0].Message);
            }
            catch (Exception e)
            {
                while (e.InnerException != null)
                    e = e.InnerException;
                Client.SendTextMessageAsync(129046388, e.Message + "\n" + e.StackTrace);
            }

            Console.WriteLine("Themes in cache: " + CachedThemes.Count);
        }

        private static LUser GetLocalUser(int id)
        {
            if (LocalUsers.All(x => x.Id != id))
                LocalUsers.Add(new LUser { Id = id });
            return LocalUsers.FirstOrDefault(x => x.Id == id);
        }

        private static void CreateOrUpdateDBUser(Telegram.Bot.Types.User u)
        {
            new Task(() =>
            {
                using (var db = new tdthemeEntities())
                {
                    try
                    {
                        var usr = db.Users.FirstOrDefault(x => x.TelegramID == u.Id);
                        if (usr == null)
                        {
                            usr = new User
                            {
                                Name = (u.FirstName + " " + u.LastName).Trim(),
                                TelegramID = u.Id,
                                Username = u.Username
                            };
                            db.Users.Add(usr);
                            db.SaveChanges();
                        }
                        else //update name information
                        {
                            usr.Name = (u.FirstName + " " + u.LastName).Trim();
                            usr.Username = u.Username;
                            db.SaveChanges();
                        }
                    }
                    catch (Exception e)
                    {
                        //TODO: Add logging
                    }
                }
            }).Start();
        }
    }
}
