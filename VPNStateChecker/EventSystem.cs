using System;
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
using System.Threading;
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
		private bool isZabbixCheck;

		private int previousSendDay = -1;
		private int errorsInSuccession = 0;
		private bool mailSystemErrorSendedToStp = false;

		public EventSystem(bool isZabbixCheck = false) {
			this.isZabbixCheck = isZabbixCheck;
			tracExeFullPath = Path.Combine(@Properties.Settings.Default.TracExePpath, "trac.exe");
			vpnSitesName = Properties.Settings.Default.VPNSites.Split(';');
			vpnUser = Properties.Settings.Default.VPNUser;
			vpnPassword = Properties.Settings.Default.VPNPassword;
			resourcesPing = Properties.Settings.Default.ResourcesToCheckPing.Split(';');
			resourcesRdpPort = Properties.Settings.Default.ResourcesToCheckRdp.Split(';');
		}

		public void CheckVpnStateByTimer() {
			System.Timers.Timer timer = new System.Timers.Timer(Properties.Settings.Default.CheckingPeriodInMinutes * 60 * 1000);
			timer.Elapsed += Timer_Elapsed;
			timer.AutoReset = true;
			timer.Start();
			Timer_Elapsed(null, null);
		}

		public void CheckVpnState(bool isSingleCheck = false) {
			string checkResult = string.Empty;
			LoggingSystem.LogMessageToFile("--- Проверка доступности VPN сервиса", ref checkResult, !isZabbixCheck);

			if (previousSendDay != DateTime.Now.Day)
				mailSystemErrorSendedToStp = false;

			string errors = string.Empty;

			if (!File.Exists(tracExeFullPath)) {
				string currentMessage = "!!! Не удается найти приложение trac.exe: " + tracExeFullPath;
				LoggingSystem.LogMessageToFile(currentMessage, ref checkResult, !isZabbixCheck);
				errors += currentMessage + Environment.NewLine;
			} else {
				string currentConfig = ExecuteCommand("info");
				LoggingSystem.LogMessageToFile("Текущая конфигурация: " + currentConfig, ref checkResult, !isZabbixCheck);

				foreach (string vpnSite in vpnSitesName) {
					if (!currentConfig.Contains(vpnSite)) {
						string currentMessage = "!!! Конфигурация не содержит подключения к хосту: " + vpnSite;
						LoggingSystem.LogMessageToFile(currentMessage, ref checkResult, !isZabbixCheck);
						errors += currentMessage + Environment.NewLine +
							"Текущая конфигурация: " + Environment.NewLine +
							currentConfig + Environment.NewLine + Environment.NewLine;
						continue;
					}
					
					LoggingSystem.LogMessageToFile("Попытка подключения к сайту: " + vpnSite, ref checkResult, !isZabbixCheck);
					string connectionResult = ExecuteCommand("connect -s " + vpnSite + " -u " + vpnUser + " -p " + vpnPassword);
					LoggingSystem.LogMessageToFile(connectionResult, ref checkResult, !isZabbixCheck);

					if (!connectionResult.Contains("Connection was successfully established") &&
						!connectionResult.Contains("Client is already connected")) {
						string currentMessage = "!!! Не удалось подключиться к сайту: " + vpnSite;
						LoggingSystem.LogMessageToFile(currentMessage, ref checkResult, !isZabbixCheck);
						errors += currentMessage + Environment.NewLine +
							"Результат выполнения команды: " + connectionResult +
							Environment.NewLine + Environment.NewLine;
						continue;
					}

					LoggingSystem.LogMessageToFile("Проверка доступности ресурсов (Ping)", ref checkResult, !isZabbixCheck);
					string pingWithError = string.Empty;
					int pingErrors = 0;
					foreach (string resource in resourcesPing) {
						LoggingSystem.LogMessageToFile("Ресурс: " + resource, ref checkResult, !isZabbixCheck);

						if (!IsPingHostOk(resource, out string resultMessage, ref checkResult)) {
							pingWithError += resource + " - " + resultMessage + Environment.NewLine;
							pingErrors++;
						}
					}

					if (pingErrors > 0) {
						string currentMessage = "!!! Используя подключение к сайту " + vpnSite +
							" не удалось получить доступ к ресурсам (PING): " + pingWithError;
						LoggingSystem.LogMessageToFile(currentMessage, ref checkResult, !isZabbixCheck);

						if (pingErrors < resourcesPing.Length / 2)
							continue;

						errors += currentMessage + Environment.NewLine + Environment.NewLine;
					}
					
					LoggingSystem.LogMessageToFile("Проверка доступности ресурсов (RDP port)", ref checkResult, !isZabbixCheck);
					string rdpWithError = string.Empty;
					int rdpErrors = 0;
					foreach (string resource in resourcesRdpPort) {
						LoggingSystem.LogMessageToFile("Ресурс: " + resource, ref checkResult, !isZabbixCheck);

						if (!IsRdpAvailable(resource, out string resultMessage, ref checkResult)) {
							rdpWithError += resource + " - " + resultMessage + Environment.NewLine;
							rdpErrors++;
						}
					}

					if (rdpErrors > 0) {
						string currentMessage = "!!! Используя подключение к сайту " + vpnSite +
							" не удалось получить доступ к ресурсам (RDP port): " + rdpWithError;
						LoggingSystem.LogMessageToFile(currentMessage, ref checkResult, !isZabbixCheck);

						if (rdpErrors < resourcesRdpPort.Length / 2)
							continue;

						errors += currentMessage + Environment.NewLine + Environment.NewLine;
					}

					Thread.Sleep(3000);
					
					LoggingSystem.LogMessageToFile("Отключение", ref checkResult, !isZabbixCheck);
					string disconnectResult = ExecuteCommand("disconnect");
					LoggingSystem.LogMessageToFile(disconnectResult, ref checkResult, !isZabbixCheck);

					if (!disconnectResult.Contains("Connection was successfully disconnected")) {
						string currentMessage = "!!! Не удалось корректно отключиться от сайта: " + vpnSite;
						LoggingSystem.LogMessageToFile(currentMessage, ref checkResult, !isZabbixCheck);
						errors += currentMessage + Environment.NewLine +
							"Результат выполнения команды: " + disconnectResult +
							Environment.NewLine + Environment.NewLine;
					}
				}
			}

			if (string.IsNullOrEmpty(errors)) {
				LoggingSystem.LogMessageToFile("--- Проверка выполнена успешно, ошибок не обнаружено", ref checkResult, !isZabbixCheck);
				mailSystemErrorSendedToStp = false;
				errorsInSuccession = 0;

				if (isZabbixCheck)
					Console.WriteLine("0");
			} else {
				LoggingSystem.LogMessageToFile("!!! Во время проверки обнаружены одна или несколько ошибок", ref checkResult, !isZabbixCheck);
				errorsInSuccession++;

				if (mailSystemErrorSendedToStp) {
					LoggingSystem.LogMessageToFile("Сообщение в СТП было отправлено ранее", ref checkResult, !isZabbixCheck);
				} else if (errorsInSuccession <3) {
					LoggingSystem.LogMessageToFile("Ошибка проявилась менее 3 раз подряд, пропуск отправки заявки", ref checkResult, !isZabbixCheck);
				} else {
					LoggingSystem.LogMessageToFile("Отправка сообщения в СТП", ref checkResult, !isZabbixCheck);
					MailSystem.SendMessage(errors);

					previousSendDay = DateTime.Now.Day;
					mailSystemErrorSendedToStp = true;
				}

				if (isZabbixCheck)
					Console.WriteLine("1");
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

		public bool IsPingHostOk(string host, out string resultMessage, ref string checkResult) {
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
									LoggingSystem.LogMessageToFile(resultMessage, ref checkResult, !isZabbixCheck);
									result = true;
									break;
								case IPStatus.TimedOut:
									resultMessage = "Connection has timed out...";
									LoggingSystem.LogMessageToFile(resultMessage, ref checkResult, !isZabbixCheck);
									result = false;
									break;
								default:
									resultMessage = "Ping failed: " + pingReply.Status.ToString();
									LoggingSystem.LogMessageToFile(resultMessage, ref checkResult, !isZabbixCheck);
									result = false;
									break;
							}
						} else
							resultMessage = "Connection failed for an unknown reason...";
							LoggingSystem.LogMessageToFile(resultMessage, ref checkResult, !isZabbixCheck);
					} catch (Exception e) {
						resultMessage = "Connection Error: " + e.Message + Environment.NewLine + e.StackTrace;
						LoggingSystem.LogMessageToFile(resultMessage, ref checkResult, !isZabbixCheck);
					}
				}
			} else {
				resultMessage = "No Internet connection found...";
				LoggingSystem.LogMessageToFile(resultMessage, ref checkResult, !isZabbixCheck);
			}
			
			return result;
		}

		private IPAddress GetIpFromHost(string host, ref string checkResult) { 
			IPAddress address = null;

			try {
				address = Dns.GetHostEntry(host).AddressList[0];
			} catch (SocketException ex) {
				string errMessage = string.Format("DNS Error: {0}", ex.Message);
				LoggingSystem.LogMessageToFile(errMessage, ref checkResult, !isZabbixCheck);
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

		private bool IsRdpAvailable(string host, out string resultMessage, ref string checkResult) {
			resultMessage = string.Empty;

			try {
				using (new TcpClient(host, 3389)) {
					LoggingSystem.LogMessageToFile("Порт 3389 для хоста " + host + " - доступен", ref checkResult, !isZabbixCheck);
					return true;
				}
			} catch (Exception e) {
				resultMessage = e.Message + Environment.NewLine + e.StackTrace;
				return false;
			}
		}
	}
}
