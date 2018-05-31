﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Timers;

namespace VPNStateChecker {
	class EventSystem {
		private readonly string tracExeFullPath;
		private readonly string[] vpnSitesName;
		private readonly string vpnUser;
		private readonly string vpnPassword;
		private readonly string[] resourcesPing;
		private readonly string[] resourcesRdpPort;

		private int previousSendDay = -1;
		private bool mailSystemErrorSendedToStp = false;

		public EventSystem() {
			tracExeFullPath = Path.Combine(@Properties.Settings.Default.TracExePpath, "trac.exe");
			vpnSitesName = Properties.Settings.Default.VPNSites.Split(';');
			vpnUser = Properties.Settings.Default.VPNUser;
			vpnPassword = Properties.Settings.Default.VPNPassword;
			resourcesPing = Properties.Settings.Default.ResourcesToCheckPing.Split(';');
			resourcesRdpPort = Properties.Settings.Default.ResourcesToCheckRdp.Split(';');
		}

		public void CheckVpnStateByTimer() {
			Timer timer = new Timer(Properties.Settings.Default.CheckingPeriodInMinutes * 60 * 1000);
			timer.Elapsed += Timer_Elapsed;
			timer.AutoReset = true;
			timer.Start();
			Timer_Elapsed(null, null);
		}

		public void CheckVpnState(bool isSingleCheck = false) {
			string checkResult = string.Empty;
			LoggingSystem.LogMessageToFile("--- Проверка доступности VPN сервиса", ref checkResult);

			if (previousSendDay != DateTime.Now.Day)
				mailSystemErrorSendedToStp = false;

			string errors = string.Empty;

			if (!File.Exists(tracExeFullPath)) {
				string currentMessage = "!!! Не удается найти приложение trac.exe: " + tracExeFullPath;
				LoggingSystem.LogMessageToFile(currentMessage, ref checkResult);
				errors += currentMessage + Environment.NewLine;
			} else {
				string currentConfig = ExecuteCommand("info");
				LoggingSystem.LogMessageToFile("Текущая конфигурация: " + currentConfig, ref checkResult);

				foreach (string vpnSite in vpnSitesName) {
					if (!currentConfig.Contains(vpnSite)) {
						string currentMessage = "!!! Конфигурация не содержит подключения к хосту: " + vpnSite;
						LoggingSystem.LogMessageToFile(currentMessage, ref checkResult);
						errors += currentMessage + Environment.NewLine +
							"Текущая конфигурация: " + Environment.NewLine +
							currentConfig + Environment.NewLine + Environment.NewLine;
						continue;
					}
					
					LoggingSystem.LogMessageToFile("Попытка подключения к сайту: " + vpnSite, ref checkResult);
					string connectionResult = ExecuteCommand("connect -s " + vpnSite + " -u " + vpnUser + " -p " + vpnPassword);
					LoggingSystem.LogMessageToFile(connectionResult, ref checkResult);

					if (!connectionResult.Contains("Connection was successfully established") &&
						!connectionResult.Contains("Client is already connected")) {
						string currentMessage = "!!! Не удалось подключиться к сайту: " + vpnSite;
						LoggingSystem.LogMessageToFile(currentMessage, ref checkResult);
						errors += currentMessage + Environment.NewLine +
							"Результат выполнения команды: " + connectionResult +
							Environment.NewLine + Environment.NewLine;
						continue;
					}

					LoggingSystem.LogMessageToFile("Проверка доступности ресурсов (Ping)", ref checkResult);
					string pingWithError = string.Empty;
					foreach (string resource in resourcesPing) {
						LoggingSystem.LogMessageToFile("Ресурс: " + resource, ref checkResult);

						if (!IsPingHostOk(resource, out string resultMessage, ref checkResult))
							pingWithError += resource + " - " + resultMessage + Environment.NewLine;
					}

					if (!string.IsNullOrEmpty(pingWithError)) {
						string currentMessage = "!!! Используя подключение к сайту " + vpnSite +
							" не удалось получить доступ к ресурсам (PING): " + pingWithError;
						LoggingSystem.LogMessageToFile(currentMessage, ref checkResult);
						errors += currentMessage + Environment.NewLine + Environment.NewLine;
					}
					
					LoggingSystem.LogMessageToFile("Проверка доступности ресурсов (RDP port)", ref checkResult);
					string rdpWithError = string.Empty;
					foreach (string resource in resourcesRdpPort) {
						LoggingSystem.LogMessageToFile("Ресурс: " + resource, ref checkResult);

						if (!IsRdpAvailable(resource, out string resultMessage, ref checkResult))
							rdpWithError += resource + " - " + resultMessage + Environment.NewLine;
					}

					if (!string.IsNullOrEmpty(rdpWithError)) {
						string currentMessage = "!!! Используя подключение к сайту " + vpnSite +
							" не удалось получить доступ к ресурсам (RDP port): " + rdpWithError;
						LoggingSystem.LogMessageToFile(currentMessage, ref checkResult);
						errors += currentMessage + Environment.NewLine + Environment.NewLine;
					}
					
					LoggingSystem.LogMessageToFile("Отключение", ref checkResult);
					string disconnectResult = ExecuteCommand("disconnect");
					LoggingSystem.LogMessageToFile(disconnectResult, ref checkResult);

					if (!disconnectResult.Contains("Connection was successfully disconnected")) {
						string currentMessage = "!!! Не удалось корректно отключиться от сайта: " + vpnSite;
						LoggingSystem.LogMessageToFile(currentMessage, ref checkResult);
						errors += currentMessage + Environment.NewLine +
							"Результат выполнения команды: " + disconnectResult +
							Environment.NewLine + Environment.NewLine;
					}
				}
			}

			if (string.IsNullOrEmpty(errors)) {
				LoggingSystem.LogMessageToFile("--- Проверка выполнена успешно, ошибок не обнаружено", ref checkResult);
				mailSystemErrorSendedToStp = false;
			} else {
				LoggingSystem.LogMessageToFile("!!! Во время проверки обнаружены одна или несколько ошибок", ref checkResult);

				if (previousSendDay == DateTime.Now.Day && mailSystemErrorSendedToStp) {
					LoggingSystem.LogMessageToFile("Сообщение в СТП было отправлено ранее", ref checkResult);
				} else {
					LoggingSystem.LogMessageToFile("Отправка сообщения в СТП", ref checkResult);
					MailSystem.SendMessage(errors);

					previousSendDay = DateTime.Now.Day;
					mailSystemErrorSendedToStp = true;
				}
			}

			if (isSingleCheck)
				MailSystem.SendMessage(checkResult, true);
		}

		private void Timer_Elapsed(object sender, ElapsedEventArgs e) {
			CheckVpnState();
		}

		private string ExecuteCommand(string command) {
			try {
				Process p = new Process();
				p.StartInfo.UseShellExecute = false;
				p.StartInfo.RedirectStandardOutput = true;
				p.StartInfo.FileName = tracExeFullPath;
				p.StartInfo.Arguments = command;
				p.Start();
				string output = p.StandardOutput.ReadToEnd();
				p.WaitForExit();
				return output;
			} catch (Exception e) {
				return e.Message + Environment.NewLine + e.StackTrace;
			}
		}

		public static bool IsPingHostOk(string host, out string resultMessage, ref string checkResult) {
			bool result = false;
			resultMessage = string.Empty;
			IPAddress address = GetIpFromHost(host, ref checkResult);
			PingOptions pingOptions = new PingOptions(128, true);
			Ping ping = new Ping();
			byte[] buffer = new byte[32];
			if (HasConnection()) {
				for (int i = 0; i < 4; i++) {
					try {
						PingReply pingReply = ping.Send(address, 1000, buffer, pingOptions);

						if (!(pingReply == null)) {
							switch (pingReply.Status) {
								case IPStatus.Success:
									resultMessage = string.Format("Reply from {0}: bytes={1} time={2}ms TTL={3}",
										pingReply.Address, pingReply.Buffer.Length, pingReply.RoundtripTime, pingReply.Options.Ttl);
									LoggingSystem.LogMessageToFile(resultMessage, ref checkResult);
									result = true;
									break;
								case IPStatus.TimedOut:
									resultMessage = "Connection has timed out...";
									LoggingSystem.LogMessageToFile(resultMessage, ref checkResult);
									result = false;
									break;
								default:
									resultMessage = "Ping failed: " + pingReply.Status.ToString();
									LoggingSystem.LogMessageToFile(resultMessage, ref checkResult);
									result = false;
									break;
							}
						} else
							resultMessage = "Connection failed for an unknown reason...";
							LoggingSystem.LogMessageToFile(resultMessage, ref checkResult);
					} catch (Exception e) {
						resultMessage = "Connection Error: " + e.Message + Environment.NewLine + e.StackTrace;
						LoggingSystem.LogMessageToFile(resultMessage, ref checkResult);
					}
				}
			} else {
				resultMessage = "No Internet connection found...";
				LoggingSystem.LogMessageToFile(resultMessage, ref checkResult);
			}
			
			return result;
		}

		private static IPAddress GetIpFromHost(string host, ref string checkResult) { 
			IPAddress address = null;

			try {
				address = Dns.GetHostEntry(host).AddressList[0];
			} catch (SocketException ex) {
				string errMessage = string.Format("DNS Error: {0}", ex.Message);
				LoggingSystem.LogMessageToFile(errMessage, ref checkResult);
			}

			return address;
		}

		[Flags]
		enum ConnectionStatusEnum : int {
			INTERNET_CONNECTION_MODEM = 0x1,
			INTERNET_CONNECTION_LAN = 0x2,
			INTERNET_CONNECTION_PROXY = 0x4,
			INTERNET_RAS_INSTALLED = 0x10,
			INTERNET_CONNECTION_OFFLINE = 0x20,
			INTERNET_CONNECTION_CONFIGURED = 0x40
		}

		[DllImport("wininet", CharSet = CharSet.Auto)]
		static extern bool InternetGetConnectedState(ref ConnectionStatusEnum flags, int dw);

		private static bool HasConnection() {
			ConnectionStatusEnum state = 0;
			InternetGetConnectedState(ref state, 0);
			if (((int) ConnectionStatusEnum.INTERNET_CONNECTION_OFFLINE & (int) state) != 0)
				return false;

			return true;
		}

		static bool IsRdpAvailable(string host, out string resultMessage, ref string checkResult) {
			resultMessage = string.Empty;

			try {
				using (new TcpClient(host, 3389)) {
					LoggingSystem.LogMessageToFile("Порт 3389 для хоста " + host + " - доступен", ref checkResult);
					return true;
				}
			} catch (Exception e) {
				resultMessage = e.Message + Environment.NewLine + e.StackTrace;
				return false;
			}
		}
	}
}
