using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Exchange.WebServices.Data;
using VPNStateChecker;

namespace VPNStateConverter {
	class Program {
		static int Main(string[] args) {
			string log = string.Empty;
			string logFileName = string.Join(", ", args);

			LoggingSystem.LogMessageToFile(
				"---Запуск VPNStateConverter", ref log, false, logFileName);
			LoggingSystem.LogMessageToFile(
				"---Возможные результаты работы: 0 - ok, 1 - error, 2 - unknown, -1 parsing error", ref log, false, logFileName);

			int retValue = 2; //0 - ok, 1 - error, 2 - unknown, -1 parsing error
			DateTime? mailCreateDate = null;
			string vpnAddress = string.Empty;

			if (args.Length != 1) {
				LoggingSystem.LogMessageToFile(
					"Для запуска необходимо передать один параметр с адресом VPN сервера", ref log, false, logFileName);
				Console.WriteLine(retValue);
				return retValue;
			} else
				vpnAddress = args[0];
			
			try {
				LoggingSystem.LogMessageToFile(
					"Подключение к почтовому ящику", ref log, false, logFileName);
				ExchangeService service = new ExchangeService(ExchangeVersion.Exchange2013) {
					Credentials = new WebCredentials(
						Properties.Settings.Default.UserName,
						Properties.Settings.Default.UserPassword,
						Properties.Settings.Default.UserDomain),
					Url = new Uri(Properties.Settings.Default.EWS)
				};

				LoggingSystem.LogMessageToFile("Проверка папки 'Входящие'", ref log, false, logFileName);
				Folder inbox = Folder.Bind(service, WellKnownFolderName.Inbox);
				if (inbox.TotalCount > 0) {
					ItemView view = new ItemView(inbox.TotalCount) {
						PropertySet = PropertySet.IdOnly
					};

					FindItemsResults<Item> results = service.FindItems(inbox.Id, view);
					LoggingSystem.LogMessageToFile("Количество писем в папке: " + results.TotalCount, ref log, false, logFileName);

					foreach (Item item in results.Items) {
						try {
							EmailMessage email = EmailMessage.Bind(service, new ItemId(item.Id.UniqueId.ToString()));

							string topic = email.ConversationTopic;

							if (!topic.ToLower().Contains(vpnAddress))
								continue;

							DateTime mailCurrentCreateDate = email.DateTimeCreated;
							LoggingSystem.LogMessageToFile("Обработка письма: '" + topic +
								"', дата создания: " + mailCurrentCreateDate.ToString(), ref log, false, logFileName);

							if (((DateTime.Now - mailCurrentCreateDate).TotalHours >= 1) ||
								(mailCreateDate.HasValue && (mailCreateDate.Value > mailCurrentCreateDate)))
								LoggingSystem.LogMessageToFile("Пропуск обработки, время создания устарело", ref log, false, logFileName);
							else {
								retValue = ParseMailTopic(topic, logFileName);
								mailCreateDate = mailCurrentCreateDate;
							}

							email.Delete(DeleteMode.HardDelete);
							email.Update(ConflictResolutionMode.AlwaysOverwrite);
						} catch (Exception e) {
							LoggingSystem.LogMessageToFile("Возникла ошибка: " + 
								e.Message + Environment.NewLine + e.StackTrace, ref log, false, logFileName);
						}
					}
				} else
					LoggingSystem.LogMessageToFile("Папка не содержит писем", ref log, false, logFileName);
			} catch (Exception e) {
				LoggingSystem.LogMessageToFile("Возникла ошибка: " + 
					e.Message + Environment.NewLine + e.StackTrace, ref log, false, logFileName);
			}

			LoggingSystem.LogMessageToFile("===Окончание обработки, возвращаемое значение: " + retValue, ref log, false, logFileName);
			Console.WriteLine(retValue);
			return retValue;
		}

		private static int ParseMailTopic(string topic, string logFileName) {
			string log = string.Empty;

			if (!topic.Contains(":")) {
				LoggingSystem.LogMessageToFile(
					"Тема письма не содержит разделитель ':', пропуск", ref log, false, logFileName);
				return -1;
			}

			string[] state = topic.Split(':');

			if (state.Length != 2) {
				LoggingSystem.LogMessageToFile(
					"Заголовок письма имеет неверный формат: '" + topic + "', пропуск", ref log, false, logFileName);
				return -1;
			}

			string vpnState = state[1].ToLower();

			if (vpnState.Equals("true"))
				return 0;
			else if (vpnState.Equals("false"))
				return 1;
			else
				LoggingSystem.LogMessageToFile(
					"Неизвестный статус: " + state, ref log, false, logFileName);

			return -1;
		}
	}
}
