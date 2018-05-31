using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace VPNStateChecker {
	public static class Program {
		public class Service : ServiceBase {
			public Service() { }

			protected override void OnStart(string[] args) {
				Start();
			}

			protected override void OnStop() {
				Stop();
			}
		}

		static void Main(string[] args) {
			if (Environment.UserInteractive || Environment.UserName.ToLower().Equals("nnkk-srv-scripts")) {
				if (args.Length == 1 && args[0].ToLower().Equals("CheckVpnService".ToLower())) {
					EventSystem eventSystem = new EventSystem();
					eventSystem.CheckVpnState(true);
				} else {
					Start();

					Console.WriteLine("Press any key to stop...");
					Console.ReadKey(true);

					Stop();
				}
			} else {
				using (Service service = new Service())
					ServiceBase.Run(service);
			}
		}

		private static void Start() {
			string empty = string.Empty;
			LoggingSystem.LogMessageToFile("Starting, cycle interval in minutes:" +
				Properties.Settings.Default.CheckingPeriodInMinutes, ref empty);

			EventSystem eventSystem = new EventSystem();
			Thread thread = new Thread(eventSystem.CheckVpnStateByTimer);
			thread.Start();
		}

		private static void Stop() {
			string empty = string.Empty;
			LoggingSystem.LogMessageToFile("Stopping", ref empty);
		}
	}
}
