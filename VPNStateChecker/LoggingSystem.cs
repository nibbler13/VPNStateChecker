using System;
using System.IO;
using System.Linq;

namespace VPNStateChecker {
	public class LoggingSystem {
		private const string LOG_FILE_NAME = "VPNStateChecker_*.log";
		private const int MAX_LOGFILES_QUANTITY = 7;

		public static void LogMessageToFile(string msg, ref string result, bool writeToConsole = true, string vpnSite = "") {
			if (writeToConsole)
				Console.WriteLine(msg);

			string logFileName = GetTodayLogFileName(vpnSite);
			result += ToLogFormat(msg, true);

			try {
				using (System.IO.StreamWriter sw = System.IO.File.AppendText(logFileName))
					sw.WriteLine(ToLogFormat(msg, false));
			} catch (Exception e) {
				Console.WriteLine("Cannot write to log file: " + logFileName + " | " + e.Message + " | " + e.StackTrace);
			}

			CheckAndCleanOldFiles();
		}

		public static string GetTodayLogFileName(string vpnSite) {
			string today = DateTime.Now.ToString("yyyyMMdd");

			if (!string.IsNullOrEmpty(vpnSite))
				vpnSite += "_";

			return AppDomain.CurrentDomain.BaseDirectory + "\\" + LOG_FILE_NAME.Replace("*", vpnSite + "*").Replace("*", today);
		}

		private static void CheckAndCleanOldFiles() {
			try {
				DirectoryInfo dirInfo = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
				FileInfo[] files = dirInfo.GetFiles(LOG_FILE_NAME).OrderBy(p => p.CreationTime).ToArray();

				if (files.Length <= MAX_LOGFILES_QUANTITY)
					return;

				for (int i = 0; i < files.Length - MAX_LOGFILES_QUANTITY; i++)
					files[i].Delete();
			} catch (Exception e) {
				Console.WriteLine("Cannot delete old lig files: " + e.Message + " | " + e.StackTrace);
			}
		}

		private static string ToLogFormat(string text, bool addNewLine) {
			return System.String.Format("{0:G}: {1}", System.DateTime.Now, text) + (addNewLine ? Environment.NewLine : "");
		}
	}
}
