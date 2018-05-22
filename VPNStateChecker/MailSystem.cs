using System.Net.Mail;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace VPNStateChecker {
	static class MailSystem {
		public static void SendErrorMessageToStp (string text = "") {
			try {
				MailAddress to = new MailAddress(Properties.Settings.Default.MailStpAddress);
				MailAddress from = new MailAddress(
					Properties.Settings.Default.MailUserName + "@" + 
					Properties.Settings.Default.MailUserDomain, "VPNStateChecker");

				string subject = "Ошибки в работе VPN";
				string body = "На группу сетевого администрирования" + Environment.NewLine +
					Environment.NewLine + "Обнаружены ошибки во время проверки работоспособности VPN: " +
					Environment.NewLine + text;

				body += Environment.NewLine + "Журнал работы во вложении" + 
					Environment.NewLine + Environment.NewLine + 
					"Это автоматически сгенерированное сообщение" +
					Environment.NewLine + "Просьба не отвечать на него" + Environment.NewLine +
					 "Имя системы: " + Environment.MachineName;

				LoggingSystem.LogMessageToFile("Отправка сообщения, тема: " + subject + ", текст: " + body);
				
				using (MailMessage message = new MailMessage()) {
					message.To.Add(to);
					message.From = from;

					message.Subject = subject;
					message.Body = body;
					if (!string.IsNullOrEmpty(Properties.Settings.Default.MailCopyAddresss))
						message.CC.Add(Properties.Settings.Default.MailCopyAddresss);
					
					message.Attachments.Add(new Attachment(LoggingSystem.GetTodayLogFileName()));

					SmtpClient client = new SmtpClient(Properties.Settings.Default.MailServer, 25);
					client.UseDefaultCredentials = false;
					client.Credentials = new System.Net.NetworkCredential(
						Properties.Settings.Default.MailUserName,
						Properties.Settings.Default.MailUserPassword,
						Properties.Settings.Default.MailUserDomain);

					client.Send(message);

					return;
				}
			} catch (Exception e) {
				LoggingSystem.LogMessageToFile(e.Message + Environment.NewLine + e.StackTrace);
			}
		}
	}
}
