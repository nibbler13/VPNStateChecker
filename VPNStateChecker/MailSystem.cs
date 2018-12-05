using System.Net.Mail;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace VPNStateChecker {
	static class MailSystem {
		public static void SendMessage (string text, bool isSingleCheck = false, bool isMailToZabbix = false) {
			try {
				MailAddress to = new MailAddress(Properties.Settings.Default.MailStpAddress);
				MailAddress from = new MailAddress(
					Properties.Settings.Default.MailUserName + "@" + 
					Properties.Settings.Default.MailUserDomain, "VPNStateChecker");

				string subject = "Ошибки в работе VPN";
				string body = "На группу сетевого администрирования" + Environment.NewLine +
					Environment.NewLine + "Обнаружены ошибки во время проверки работоспособности VPN: " +
					Environment.NewLine + text;

				body += Environment.NewLine + "Журнал работы во вложении";

				if (isSingleCheck) {
					to = new MailAddress(Properties.Settings.Default.MailToSingleCheck);
					subject = "Результаты проверки сервиса VPN - " +
							(text.Contains("!") ? " Внимание! Обнаружены ошибки!" : "ошибок не обнаружено");
					body = text;
				} else if (isMailToZabbix) {
					to = new MailAddress(Properties.Settings.Default.MailAddressToZabbix);
					subject = text;
					body = text;
				}

				body = body + Environment.NewLine + Environment.NewLine +
					"Это автоматически сгенерированное сообщение" +
					Environment.NewLine + "Просьба не отвечать на него" + Environment.NewLine +
					 "Имя системы: " + Environment.MachineName;

				string empty = string.Empty;
				LoggingSystem.LogMessageToFile("Отправка сообщения, тема: " + subject + ", текст: " + body, ref empty);
				
				using (MailMessage message = new MailMessage()) {
					message.To.Add(to);
					message.From = from;

					message.Subject = subject;
					message.Body = body;
					if (!string.IsNullOrEmpty(Properties.Settings.Default.MailCopyAddresss) && !isMailToZabbix)
						foreach (string address in Properties.Settings.Default.MailCopyAddresss.Split(';'))
							message.CC.Add(address);

					if (!isSingleCheck && !isMailToZabbix)
						message.Attachments.Add(new Attachment(LoggingSystem.GetTodayLogFileName(string.Empty)));

					SmtpClient client = new SmtpClient(Properties.Settings.Default.MailServer, 25) {
						UseDefaultCredentials = false,
						Credentials = new System.Net.NetworkCredential(
						Properties.Settings.Default.MailUserName,
						Properties.Settings.Default.MailUserPassword,
						Properties.Settings.Default.MailUserDomain)
					};

					client.Send(message);

					return;
				}
			} catch (Exception e) {
				string empty = string.Empty;
				LoggingSystem.LogMessageToFile(e.Message + Environment.NewLine + e.StackTrace, ref empty);
			}
		}
	}
}
