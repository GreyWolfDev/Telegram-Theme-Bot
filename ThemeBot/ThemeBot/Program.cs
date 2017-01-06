using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.IO;
using System.Linq;
using System.Net.Mail;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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
        static void Main(string[] args)
        {
            new Thread(InitializeBot).Start();
            Thread.Sleep(-1);
        }

        private static void InitializeBot()
        {
            Client = new TelegramBotClient(RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey("SOFTWARE\\ThemeBot").GetValue("ProductionAPI").ToString());
            Client.OnMessage += ClientOnOnMessage;
            Client.OnCallbackQuery += ClientOnOnCallbackQuery;
            Client.OnInlineQuery += ClientOnOnInlineQuery;
            Client.OnInlineResultChosen += ClientOnOnInlineResultChosen;
            Client.StartReceiving();
        }

        private static void ClientOnOnInlineResultChosen(object sender, ChosenInlineResultEventArgs chosenInlineResultEventArgs)
        {

        }

        private static void ClientOnOnInlineQuery(object sender, InlineQueryEventArgs inlineQueryEventArgs)
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
                    lu.ResultSet = db.Themes.Where(x => x.Description.ToLower().Contains(search) || x.Name.ToLower().Contains(search)).Include(x => x.User).ToList();
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
                            Caption = $"{x.Name}\n{x.Description}" + (x.ShowOwnerName ? $"\nBy {x.User.Name}" + (x.ShowOwnerUsername ? $" (@{x.User.Username})" : "") : ""),
                            FileId = x.Photo_Id,
                            Id = x.Id.ToString(),
                            //InputMessageContent = new InputTextMessageContent { MessageText = x.Description + "text", DisableWebPagePreview = true },
                            Title = x.Name,
                            ReplyMarkup = new InlineKeyboardMarkup(new[] { new InlineKeyboardButton("Get This Theme") { Url = "https://t.me/tthemebot?start=t" + x.Id }, })
                        }).ToArray();
#else
                    x => new InlineQueryResultArticle()
                    {
                        Description = x.Description,
                        Id = x.Id.ToString(),
                        InputMessageContent = new InputTextMessageContent { MessageText = $"{x.Name}\n{x.Description}" + (x.ShowOwnerName ? $"\nBy {x.User.Name}" + (x.ShowOwnerUsername ? $" (@{x.User.Username})" : "") : ""), DisableWebPagePreview = true },
                        Title = x.Name,
                        ReplyMarkup = new InlineKeyboardMarkup(new[] { new InlineKeyboardButton("Get This Theme") { Url = "https://t.me/tthemebot?start=t" + x.Id }, })
                    }).ToArray();
#endif
            offset += 5;
            var result = Client.AnswerInlineQueryAsync(q.Id, toSend, nextOffset: offset.ToString()).Result;
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
                        if (lu.ThemeCreating.ShowOwnerName & !String.IsNullOrEmpty(q.From.Username))
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
                            lu.ThemeCreating.ShowOwnerUsername = false;
                            SaveTheme(lu);
                        }
                    }
                    else
                    {
                        lu.ThemeUpdating.ShowOwnerName = q.Data.Split('|')[1] == "yes";
                        if (lu.ThemeUpdating.ShowOwnerName & !String.IsNullOrEmpty(q.From.Username))
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
                            Client.EditMessageTextAsync(q.From.Id, q.Message.MessageId, "Enjoy your theme!");
                        }
                        else
                        {
                            using (var db = new tdthemeEntities())
                            {
                                var rTheme = db.Themes.FirstOrDefault(x => x.Id.ToString() == args[1]);
                                var rUser = db.Users.FirstOrDefault(x => x.TelegramID == q.From.Id);
                                var r = new Rating
                                {
                                    ThemeId = rTheme.Id,
                                    UserId = rUser.Id,
                                    TimeStamp = DateTime.UtcNow,
                                    Rating1 = int.Parse(args[2])
                                };
                                db.Ratings.Add(r);
                                db.SaveChanges();
                            }
                            Client.EditMessageTextAsync(q.From.Id, q.Message.MessageId, "Thank you for rating!");
                        }

                        break;
                }


            }
            catch (Exception e)
            {
                //TODO: add logging
            }
        }

        private static void SaveTheme(LUser lu, bool update = false)
        {
            using (var db = new tdthemeEntities())
            {
                if (!update)
                {
                    //get the database user
                    var thisUser = db.Users.FirstOrDefault(x => x.TelegramID == lu.Id);
                    lu.ThemeCreating.LastUpdated = DateTime.UtcNow;
                    thisUser.Themes.Add(lu.ThemeCreating);
                    db.SaveChanges();
                }
                else
                {
                    var t = db.Themes.FirstOrDefault(x => x.Id == lu.ThemeUpdating.Id);
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
                }

                Client.SendTextMessageAsync(lu.Id, "Your theme is ready!");
                LocalUsers.Remove(lu);
                lu = null;
            }
        }

        private static async void ClientOnOnMessage(object sender, MessageEventArgs messageEventArgs)
        {
            new Thread(() => HandleMessage(messageEventArgs.Message)).Start();
        }

        private static void HandleMessage(Message m)
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
                                if (arg.StartsWith("t"))
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
                                        Client.SendDocumentAsync(m.Chat.Id, new FileToSend(theme.File_Id), theme.Name);
                                        Client.SendTextMessageAsync(m.From.Id, "Take a moment to rate this theme:", replyMarkup:
                                            new InlineKeyboardMarkup(new[] {
                                                Enumerable.Range(1, 5).Select(x => new InlineKeyboardButton(x.ToString(), $"rate|{theme.Id}|{x}")).ToArray(),
                                                new []
                                            {
                                                new InlineKeyboardButton("No Thanks", "rate|no")
                                            }, }));
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
                                    //"/cancel - Cancel the current operation\n\n" +
                                    "You can also search for themes inline");
                            }
                            break;
                        case "help":

                            Client.SendTextMessageAsync(m.Chat.Id, "Available commands:\n" +
                                                                   "/help - Show this list\n" +
                                                                   "/newtheme - Upload a new theme to the catalog\n" +
                                                                   "/edittheme - Edit one of your themes\n" +
                                                                   "/deletetheme - Delete a theme\n" +
                                                                   //"/cancel - Cancel the current operation\n\n" +
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
                                                 usr.Themes.Select(x => new[] { new InlineKeyboardButton(x.Name, $"update|{x.Id}") }).ToArray()
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
                                                 usr.Themes.Select(x => new[] { new InlineKeyboardButton(x.Name, $"delete|{x.Id}") }).ToArray()
                                            ));
                                        break;
                                    }
                                }
                                Client.SendTextMessageAsync(m.From.Id,
                                    "You don't have any themes to update!  Upload new themes with /newtheme");
                            }
                            break;
                        case "cancel":
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


                    }
                }
                else
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
                                    "Alright, now enter a short description of your theme");
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
                                Client.SendTextMessageAsync(lu.Id, "Great, now upload the file to me.");
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
                        if (lu.ThemeCreating != null)
                        {
                            lu.ThemeCreating.FileName = filename;
                            //get the file itself
                            //var fs = new FileStream(filename, FileMode.Create);
                            //var file = Client.GetFileAsync(m.Document.FileId, fs).Result;
                            //fs.Close();
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
                            "Got it.  Now a couple simple questions.\nFirst, do you want your name to be shown in the search result for your theme?", replyMarkup: new InlineKeyboardMarkup(new[] { new InlineKeyboardButton("Yes", "showname|yes"), new InlineKeyboardButton("No", "showname|no") }));

                        break;
                    default:
                        //didn't ask for a photo...
                        break;
                }
            }
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
