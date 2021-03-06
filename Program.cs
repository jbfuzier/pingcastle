﻿//
// Copyright (c) Ping Castle. All rights reserved.
// https://www.pingcastle.com
//
// Licensed under the Non-Profit OSL. See LICENSE file in the project root for full license information.
//
using PingCastle.ADWS;
using PingCastle.Graph.Database;
using PingCastle.Export;
using PingCastle.Healthcheck;
using PingCastle.Scanners;
using PingCastle.misc;
using PingCastle.RPC;
using PingCastle.Reporting;
using PingCastle.shares;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Security.Principal;
using PingCastle.Report;
using PingCastle.Data;

namespace PingCastle
{
	[LicenseProvider(typeof(PingCastle.ADHealthCheckingLicenseProvider))]
	public class Program : IPingCastleLicenseInfo
	{
		bool PerformHealthCheckReport = false;
		bool PerformHealthCheckConsolidation = false;
		bool PerformGraphConsolidation = false;
		bool PerformGenerateKey = false;
		bool PerformCarto = false;
		bool PerformAdvancedLive;
		bool PerformUploadAllReport;
		private bool PerformRegenerateReport;
		private bool PerformHealthCheckReloadReport;
		bool PerformHealthCheckGenerateDemoReports;
		bool PerformScanner = false;
		Tasks tasks = new Tasks();


		public static void Main(string[] args)
		{
			try
			{
				AppDomain.CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler(CurrentDomain_UnhandledException);
				AppDomain.CurrentDomain.AssemblyResolve += new ResolveEventHandler(CurrentDomain_AssemblyResolve);
				Trace.WriteLine("Running on dotnet:" + Environment.Version);
				Program program = new Program();
				program.Run(args);
				if (program.tasks.InteractiveMode)
				{
					Console.WriteLine("=============================================================================");
					Console.WriteLine("Program launched in interactive mode - press any key to terminate the program");
					Console.WriteLine("=============================================================================");
					Console.ReadKey();
				}
			}
			catch (Exception ex)
			{
				Tasks.DisplayException("main program", ex);
			}
		}

		static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
		{
			Tasks.DisplayException("application domain", e.ExceptionObject as Exception);
		}

		static Assembly CurrentDomain_AssemblyResolve(object sender, ResolveEventArgs args)
		{
			// hook required for "System.Runtime.Serialization.ContractNamespaceAttribute"
			var name = new AssemblyName(args.Name);
			Trace.WriteLine("Needing assembly " + name + " unknown (" + args.Name + ")");
			return null;
		}

		private void Run(string[] args)
		{
			ADHealthCheckingLicense license = null;
			Version version = Assembly.GetExecutingAssembly().GetName().Version;
			Trace.WriteLine("PingCastle version " + version.ToString(4));
			for (int i = 0; i < args.Length; i++)
			{
				if (args[i].Equals("--debug-license", StringComparison.InvariantCultureIgnoreCase))
				{
					EnableLogConsole();
				}
				else if (args[i].Equals("--license", StringComparison.InvariantCultureIgnoreCase) && i + 1 < args.Length)
				{
					_serialNumber = args[++i];
				}
			}
			Trace.WriteLine("Starting the license checking");
			try
			{
				license = LicenseManager.Validate(typeof(Program), this) as ADHealthCheckingLicense;
			}
			catch (Exception ex)
			{
				Trace.WriteLine("the license check failed - please check that the .config file is in the same directory");
				WriteInRed(ex.Message);
				if (args.Length == 0)
				{
					Console.WriteLine("=============================================================================");
					Console.WriteLine("Program launched in interactive mode - press any key to terminate the program");
					Console.WriteLine("=============================================================================");
					Console.ReadKey();
				}
				return;
			}
			Trace.WriteLine("License checked");
			if (license.EndTime < DateTime.Now)
			{
				WriteInRed("The program is unsupported since: " + license.EndTime.ToString("u") + ")");
				if (args.Length == 0)
				{
					Console.WriteLine("=============================================================================");
					Console.WriteLine("Program launched in interactive mode - press any key to terminate the program");
					Console.WriteLine("=============================================================================");
					Console.ReadKey();
				}
				return;
			}
			if (license.EndTime < DateTime.MaxValue)
			{
				Console.WriteLine();
			}
			tasks.License = license;
			ConsoleMenu.Header = @"|:.      PingCastle (Version " + version.ToString(4) + @"     " + ConsoleMenu.GetBuildDateTime(Assembly.GetExecutingAssembly()) + @")
|  #:.   Get Active Directory Security at 80% in 20% of the time
# @@  >  " + (license.EndTime < DateTime.MaxValue ? "End of support: " + license.EndTime.ToShortDateString() : "") + @"
| @@@:   
: .#                                 Vincent LE TOUX (contact@pingcastle.com)
.:                                                 https://www.pingcastle.com";
			if (!ParseCommandLine(args))
				return;
			// Trace to file or console may be enabled here
			Trace.WriteLine("[New run]" + DateTime.Now.ToString("u"));
			Trace.WriteLine("PingCastle version " + version.ToString(4));
			Trace.WriteLine("Running on dotnet:" + Environment.Version);
			if (!String.IsNullOrEmpty(license.DomainLimitation) && !Tasks.compareStringWithWildcard(license.DomainLimitation, tasks.Server))
			{
				WriteInRed("Limitations applies to the --server argument (" + license.DomainLimitation + ")");
				return;
			}
			if (!String.IsNullOrEmpty(license.CustomerNotice))
			{
				Console.WriteLine(license.CustomerNotice);
			}
			if (PerformGenerateKey)
			{
				if (!tasks.GenerateKeyTask()) return;
			}
			if (PerformScanner)
			{
				if (!tasks.ScannerTask()) return;
			}
			if (PerformCarto)
			{
				if (!tasks.CartoTask(PerformHealthCheckGenerateDemoReports)) return;
			}
			if (PerformHealthCheckReport)
			{
				if (!tasks.AnalysisTask<HealthcheckData>()) return;
			}
			if (PerformAdvancedLive)
			{
				if (!tasks.AnalysisTask<CompromiseGraphData>()) return;
			}
			if (PerformHealthCheckConsolidation || (PerformHealthCheckReport && tasks.Server == "*" && tasks.InteractiveMode))
			{
				if (!tasks.ConsolidationTask<HealthcheckData>()) return;
			}
			if (PerformGraphConsolidation || (PerformAdvancedLive && tasks.Server == "*" && tasks.InteractiveMode))
			{
				if (!tasks.ConsolidationTask<CompromiseGraphData>()) return;
			}
			if (PerformRegenerateReport)
			{
				if (!tasks.RegenerateHtmlTask()) return;
			}
			if (PerformHealthCheckReloadReport)
			{
				if (!tasks.ReloadXmlReport()) return;
			}
			if (PerformHealthCheckGenerateDemoReports && !PerformCarto)
			{
				if (!tasks.GenerateDemoReportTask()) return;
			}
			if (PerformUploadAllReport)
			{
				if (!tasks.UploadAllReportInCurrentDirectory()) return;
			}
			tasks.CompleteTasks();
		}

		string _serialNumber;
		public string GetSerialNumber()
		{
			if (String.IsNullOrEmpty(_serialNumber))
			{
				try
				{
					_serialNumber = ADHealthCheckingLicenseSettings.Settings.License;
				}
				catch (Exception ex)
				{
					Trace.WriteLine("Exception when getting the license string");
					Trace.WriteLine(ex.Message);
					Trace.WriteLine(ex.StackTrace);
					if (ex.InnerException != null)
					{
						Trace.WriteLine(ex.InnerException.Message);
						Trace.WriteLine(ex.InnerException.StackTrace);
					}
					throw new PingCastleException("Unable to load the license from the .config file. Check that all files have been copied in the same directory");
				}
			}
			return _serialNumber;
		}

		private void WriteInRed(string data)
		{
			Console.ForegroundColor = ConsoleColor.Red;
			Console.WriteLine(data);
			Trace.WriteLine("[Red]" + data);
			Console.ResetColor();
		}

		private string GetCurrentDomain()
		{
			return IPGlobalProperties.GetIPGlobalProperties().DomainName;
		}

		// parse command line arguments
		private bool ParseCommandLine(string[] args)
		{
			string user = null;
			string userdomain = null;
			string password = null;
			bool delayedInteractiveMode = false;
			if (args.Length == 0)
			{
				if (!RunInteractiveMode())
					return false;
			}
			else
			{
				Trace.WriteLine("Before parsing arguments");
				for (int i = 0; i < args.Length; i++)
				{
					switch (args[i])
					{
						case "--api-endpoint":
							if (i + 1 >= args.Length)
							{
								WriteInRed("argument for --api-endpoint is mandatory");
								return false;
							}
							tasks.apiEndpoint = args[++i];
							{
								Uri res;
								if (!Uri.TryCreate(tasks.apiEndpoint, UriKind.Absolute, out res))
								{
									WriteInRed("unable to convert api-endpoint into an URI");
									return false;
								}
							}
							break;
						case "--api-key":
							if (i + 1 >= args.Length)
							{
								WriteInRed("argument for --api-key is mandatory");
								return false;
							}
							tasks.apiKey = args[++i];
							break;
						case "--carto":
							PerformCarto = true;
							break;
						case "--center-on":
							if (i + 1 >= args.Length)
							{
								WriteInRed("argument for --center-on is mandatory");
								return false;
							}
							tasks.CenterDomainForSimpliedGraph = args[++i];
							break;
						case "--debug-license":
							break;
						case "--demo-reports":
							PerformHealthCheckGenerateDemoReports = true;
							break;
						case "--encrypt":
							tasks.EncryptReport = true;
							break;
						case "--foreigndomain":
							if (i + 1 >= args.Length)
							{
								WriteInRed("argument for --foreigndomain is mandatory");
								return false;
							}
							ForeignUsersScanner.EnumInboundSid = args[++i];
							break;
						case "--explore-trust":
							tasks.ExploreTerminalDomains = true;
							break;
						case "--explore-forest-trust":
							tasks.ExploreForestTrust = true;
							break;
						case "--explore-exception":
							if (i + 1 >= args.Length)
							{
								WriteInRed("argument for --explore-exception is mandatory");
								return false;
							}
							tasks.DomainToNotExplore = new List<string>(args[++i].Split(','));
							break;
						case "--filter-date":
							if (i + 1 >= args.Length)
							{
								WriteInRed("argument for --filter-date is mandatory");
								return false;
							}
							if (!DateTime.TryParse(args[++i], out tasks.FilterReportDate))
							{
								WriteInRed("Unable to parse the date \"" + args[i] + "\" - try entering 2016-01-01");
								return false;
							}
							break;
						case "--regen-report":
							PerformRegenerateReport = true;
							if (i + 1 >= args.Length)
							{
								WriteInRed("argument for --regen-report is mandatory");
								return false;
							}
							tasks.FileOrDirectory = args[++i];
							break;
						case "--generate-key":
							PerformGenerateKey = true;
							break;
						case "--graph":
							PerformAdvancedLive = true;
							break;
						case "--healthcheck":
							PerformHealthCheckReport = true;
							break;
						case "--hc-conso":
							PerformHealthCheckConsolidation = true;
							break;
						case "--cg-conso":
							PerformGraphConsolidation = true;
							break;
						case "--help":
							DisplayHelp();
							return false;
						case "--interactive":
							delayedInteractiveMode = true;
							break;
						case "--level":
							if (i + 1 >= args.Length)
							{
								WriteInRed("argument for --level is mandatory");
								return false;
							}
							try
							{
								tasks.ExportLevel = (PingCastleReportDataExportLevel)Enum.Parse(typeof(PingCastleReportDataExportLevel), args[++i]);
							}
							catch (Exception)
							{
								WriteInRed("Unable to parse the level [" + args[i] + "] to one of the predefined value (" + String.Join(",", Enum.GetNames(typeof(PingCastleReportDataExportLevel))) + ")");
								return false;
							}
							break;
						case "--license":
							i++;
							break;
						case "--log":
							EnableLogFile();
							break;
						case "--log-console":
							EnableLogConsole();
							break;
						case "--max-nodes":
							if (i + 1 >= args.Length)
							{
								WriteInRed("argument for --max-nodes is mandatory");
								return false;
							}
							{
								int maxNodes;
								if (!int.TryParse(args[++i], out maxNodes))
								{
									WriteInRed("argument for --max-nodes is not a valid value (typically: 1000)");
									return false;
								}
								ReportGenerator.MaxNodes = maxNodes;
							}
							break;
						case "--max-depth":
							if (i + 1 >= args.Length)
							{
								WriteInRed("argument for --max-depth is mandatory");
								return false;
							}
							{
								int maxDepth;
								if (!int.TryParse(args[++i], out maxDepth))
								{
									WriteInRed("argument for --max-depth is not a valid value (typically: 30)");
									return false;
								}
								ReportGenerator.MaxDepth = maxDepth;
							}
							break;
						case "--no-enum-limit":
							ReportHealthCheckSingle.MaxNumberUsersInHtmlReport = int.MaxValue;
							break;
						case "--node":
							if (i + 1 >= args.Length)
							{
								WriteInRed("argument for --node is mandatory");
								return false;
							}
							tasks.NodesToInvestigate = new List<string>(Regex.Split(args[++i], @"(?<!(?<!\\)*\\)\,"));
							break;
						case "--nodes":
							if (i + 1 >= args.Length)
							{
								WriteInRed("argument for --nodes is mandatory");
								return false;
							}
							tasks.NodesToInvestigate = new List<string>(File.ReadAllLines(args[++i]));
							break;
						case "--notifyMail":
							if (i + 1 >= args.Length)
							{
								WriteInRed("argument for --notifyMail is mandatory");
								return false;
							}
							tasks.mailNotification = args[++i];
							break;
						case "--nslimit":
							if (i + 1 >= args.Length)
							{
								WriteInRed("argument for --nslimit is mandatory");
								return false;
							}
							if (!int.TryParse(args[++i], out NullSessionScanner.NullSessionEnumerationLimit))
							{
								WriteInRed("argument for --nslimit is not a valid value (typically: 5)");
								return false;
							}
							break;
						case "--password":
							if (i + 1 >= args.Length)
							{
								WriteInRed("argument for --password is mandatory");
								return false;
							}
							password = args[++i];
							break;
						case "--port":
							if (i + 1 >= args.Length)
							{
								WriteInRed("argument for --port is mandatory");
								return false;
							}
							if (!int.TryParse(args[++i], out tasks.Port))
							{
								WriteInRed("argument for --port is not a valid value (typically: 9389)");
								return false;
							}
							break;
						case "--protocol":
							if (i + 1 >= args.Length)
							{
								WriteInRed("argument for --protocol is mandatory");
								return false;
							}
							try
							{
								ADWebService.ConnectionType = (ADConnectionType)Enum.Parse(typeof(ADConnectionType), args[++i]);
							}
							catch (Exception ex)
							{
								Trace.WriteLine(ex.Message);
								WriteInRed("Unable to parse the protocol [" + args[i] + "] to one of the predefined value (" + String.Join(",", Enum.GetNames(typeof(ADConnectionType))) + ")");
								return false;
							}
							break;
						case "--reachable":
							tasks.AnalyzeReachableDomains = true;
							break;
						case "--scanner":
							if (i + 1 >= args.Length)
							{
								WriteInRed("argument for --scanner is mandatory");
								return false;
							}
							{
								var scanners = PingCastleFactory.GetAllScanners();
								string scannername = args[++i];
								if (!scanners.ContainsKey(scannername))
								{
									string list = null;
									var allscanners = new List<string>(scanners.Keys);
									allscanners.Sort();
									foreach (string name in allscanners)
									{
										if (list != null)
											list += ",";
										list += name;
									}
									WriteInRed("Unsupported scannername - available scanners are:" + list);
								}
								tasks.Scanner = scanners[scannername];
								PerformScanner = true;
							}
							break;
						case "--scmode-single":
							ScannerBase.ScanningMode = 1;
							break;
						case "--sendxmlTo":
						case "--sendXmlTo":
							if (i + 1 >= args.Length)
							{
								WriteInRed("argument for --sendXmlTo is mandatory");
								return false;
							}
							tasks.sendXmlTo = args[++i];
							break;
						case "--sendhtmlto":
						case "--sendHtmlTo":
							if (i + 1 >= args.Length)
							{
								WriteInRed("argument for --sendHtmlTo is mandatory");
								return false;
							}
							tasks.sendHtmlTo = args[++i];
							break;
						case "--sendallto":
						case "--sendAllTo":
							if (i + 1 >= args.Length)
							{
								WriteInRed("argument for --sendAllTo is mandatory");
								return false;
							}
							tasks.sendAllTo = args[++i];
							break;
						case "--server":
							if (i + 1 >= args.Length)
							{
								WriteInRed("argument for --server is mandatory");
								return false;
							}
							tasks.Server = args[++i];
							break;
						case "--skip-null-session":
							HealthcheckAnalyzer.SkipNullSession = true;
							break;
						case "--reload-report":
						case "--slim-report":
							PerformHealthCheckReloadReport = true;
							if (i + 1 >= args.Length)
							{
								WriteInRed("argument for --slim-report is mandatory");
								return false;
							}
							tasks.FileOrDirectory = args[++i];
							break;
						case "--smtplogin":
							if (i + 1 >= args.Length)
							{
								WriteInRed("argument for --smtplogin is mandatory");
								return false;
							}
							tasks.smtpLogin = args[++i];
							break;
						case "--smtppass":
							if (i + 1 >= args.Length)
							{
								WriteInRed("argument for --smtppass is mandatory");
								return false;
							}
							tasks.smtpPassword = args[++i];
							break;
						case "--smtptls":
							tasks.smtpTls = true;
							break;
						case "--upload-all-reports":
							PerformUploadAllReport = true;
							break;
						case "--user":
							if (i + 1 >= args.Length)
							{
								WriteInRed("argument for --user is mandatory");
								return false;
							}
							i++;
							if (args[i].Contains("\\"))
							{
								int pos = args[i].IndexOf('\\');
								userdomain = args[i].Substring(0, pos);
								user = args[i].Substring(pos + 1);
							}
							else
							{
								user = args[i];
							}
							break;
						case "--webdirectory":
							if (i + 1 >= args.Length)
							{
								WriteInRed("argument for --webdirectory is mandatory");
								return false;
							}
							tasks.sharepointdirectory = args[++i];
							break;
						case "--webuser":
							if (i + 1 >= args.Length)
							{
								WriteInRed("argument for --webuser is mandatory");
								return false;
							}
							tasks.sharepointuser = args[++i];
							break;
						case "--webpassword":
							if (i + 1 >= args.Length)
							{
								WriteInRed("argument for --webpassword is mandatory");
								return false;
							}
							tasks.sharepointpassword = args[++i];
							break;
						case "--xmls":
							if (i + 1 >= args.Length)
							{
								WriteInRed("argument for --xmls is mandatory");
								return false;
							}
							tasks.FileOrDirectory = args[++i];
							break;
						default:
							WriteInRed("unknow argument: " + args[i]);
							DisplayHelp();
							return false;
					}
				}
				Trace.WriteLine("After parsing arguments");
			}
			if (!PerformHealthCheckReport && !PerformHealthCheckConsolidation && !PerformGraphConsolidation
				&& !PerformRegenerateReport && !PerformHealthCheckReloadReport && !delayedInteractiveMode
				&& !PerformScanner
				&& !PerformGenerateKey && !PerformHealthCheckGenerateDemoReports && !PerformCarto && !PerformAdvancedLive
				&& !PerformUploadAllReport)
			{
				WriteInRed("You must choose at least one value among --healthcheck --hc-conso --advanced-export --advanced-report --nullsession --carto");
				DisplayHelp();
				return false;
			}
			Trace.WriteLine("Things to do OK");
			if (delayedInteractiveMode)
			{
				RunInteractiveMode();
			}
			if (PerformHealthCheckReport || PerformScanner || PerformAdvancedLive)
			{
				if (String.IsNullOrEmpty(tasks.Server))
				{
					tasks.Server = GetCurrentDomain();
					if (String.IsNullOrEmpty(tasks.Server))
					{
						WriteInRed("This computer is not connected to a domain. The program couldn't guess the domain or server to connect.");
						WriteInRed("Please run again this program with the flag --server <my.domain.com> or --server <mydomaincontroller.my.domain.com>");
						DisplayHelp();
						return false;
					}
				}
				if (user != null)
				{
					if (password == null)
						password = AskCredential();
					if (String.IsNullOrEmpty(userdomain))
					{
						tasks.Credential = new NetworkCredential(user, password);
					}
					else
					{
						tasks.Credential = new NetworkCredential(user, password, userdomain);
					}
				}
			}
			if (PerformHealthCheckConsolidation || PerformGraphConsolidation)
			{
				if (String.IsNullOrEmpty(tasks.FileOrDirectory))
				{
					tasks.FileOrDirectory = Directory.GetCurrentDirectory();
				}
				else
				{
					if (!Directory.Exists(tasks.FileOrDirectory))
					{
						WriteInRed("The path specified by --xmls isn't a directory");
						DisplayHelp();
						return false;
					}
				}
			}
			return true;
		}

		private void EnableLogFile()
		{
			Trace.AutoFlush = true;
			TextWriterTraceListener listener = new TextWriterTraceListener("trace.log");
			Trace.Listeners.Add(listener);
		}

		private void EnableLogConsole()
		{
			Trace.AutoFlush = true;
			TextWriterTraceListener listener = new TextWriterTraceListener(Console.Out);
			Trace.Listeners.Add(listener);
		}

		private string AskCredential()
		{
			StringBuilder builder = new StringBuilder();
			Console.WriteLine("Enter password: ");
			ConsoleKeyInfo nextKey = Console.ReadKey(true);

			while (nextKey.Key != ConsoleKey.Enter)
			{
				if (nextKey.Key == ConsoleKey.Backspace)
				{
					if (builder.Length > 0)
					{
						builder.Remove(builder.Length - 1, 1);
						// erase the last * as well
						Console.Write(nextKey.KeyChar);
						Console.Write(" ");
						Console.Write(nextKey.KeyChar);
					}
				}
				else
				{
					builder.Append(nextKey.KeyChar);
					Console.Write("*");
				}
				nextKey = Console.ReadKey(true);
			}
			Console.WriteLine();
			return builder.ToString();
		}

		private enum DisplayState
		{
			Exit,
			MainMenu,
			ScannerMenu,
			AskForServer,
			Run,
			AvancedMenu,
			AskForAdditionalUsers,
			AskForScannerParameter,
			ProtocolMenu,
			AskForFile,
		}

		DisplayState DisplayMainMenu()
		{
			PerformHealthCheckReport = false;
			PerformGraphConsolidation = false;
			PerformAdvancedLive = false;
			PerformCarto = false;
			PerformHealthCheckConsolidation = false;
			PerformScanner = false;

			List<KeyValuePair<string, string>> choices = new List<KeyValuePair<string, string>>() {
				new KeyValuePair<string, string>("healthcheck","Score the risk of a domain"),
				new KeyValuePair<string, string>("graph","Analyze admin groups and delegations with diagrams"),
				new KeyValuePair<string, string>("conso","Aggregate multiple reports into a single one"),
				new KeyValuePair<string, string>("carto","Build a map of all interconnected domains"),
				new KeyValuePair<string, string>("scanner","Perform specific security checks on workstations"),
				new KeyValuePair<string, string>("advanced","Open the advanced menu"),
			};

			ConsoleMenu.Title = "What do you want to do?";
			ConsoleMenu.Information = "Using interactive mode.\r\nDo not forget that there are other command line switches like --help that you can use";
			int choice = ConsoleMenu.SelectMenu(choices);
			if (choice == 0)
				return DisplayState.Exit;

			string whattodo = choices[choice - 1].Key;
			switch (whattodo)
			{
				default:
				case "healthcheck":
					PerformHealthCheckReport = true;
					return DisplayState.AskForServer;
				case "graph":
					PerformAdvancedLive = true;
					return DisplayState.AskForServer;
				case "carto":
					PerformCarto = true;
					return DisplayState.AskForServer;
				case "conso":
					PerformHealthCheckConsolidation = true;
					PerformGraphConsolidation = true;
					return DisplayState.Run;
				case "scanner":
					PerformScanner = true;
					return DisplayState.ScannerMenu;
				case "advanced":
					return DisplayState.AvancedMenu;
			}
		}

		DisplayState DisplayScannerMenu()
		{
			var scanners = PingCastleFactory.GetAllScanners();

			var choices = new List<KeyValuePair<string, string>>();
			foreach (var scanner in scanners)
			{
				Type scannerType = scanner.Value;
				IScanner iscanner = PingCastleFactory.LoadScanner(scannerType);
				string description = iscanner.Description;
				choices.Add(new KeyValuePair<string, string>(scanner.Key, description));
			}
			choices.Sort((KeyValuePair<string, string> a, KeyValuePair<string, string> b)
				=>
				{
					return String.Compare(a.Key, b.Key);
				}
			);
			ConsoleMenu.Notice = "WARNING: Checking a lot of workstations may raise security alerts.";
			ConsoleMenu.Title = "Select a scanner";
			ConsoleMenu.Information = "What scanner whould you like to run ?";
			int choice = ConsoleMenu.SelectMenuCompact(choices, 1);
			if (choice == 0)
				return DisplayState.Exit;
			tasks.Scanner = scanners[choices[choice - 1].Key];
			return DisplayState.AskForScannerParameter;
		}

		DisplayState DisplayAskForScannerParameter()
		{
			IScanner iscannerAddParam = PingCastleFactory.LoadScanner(tasks.Scanner);
			if (!iscannerAddParam.QueryForAdditionalParameterInInteractiveMode())
				return DisplayState.Exit;
			return DisplayState.AskForServer;
		}

		DisplayState DisplayAskServer()
		{
			string defaultDomain = tasks.Server;
			if (String.IsNullOrEmpty(defaultDomain))
				defaultDomain = GetCurrentDomain();
			while (true)
			{
				if (!String.IsNullOrEmpty(defaultDomain))
				{
					ConsoleMenu.Information = "Please specify the domain or server to investigate (default:" + defaultDomain + ")";
				}
				else
				{
					ConsoleMenu.Information = "Please specify the domain or server to investigate:";
				}
				ConsoleMenu.Title = "Select a domain or server";
				tasks.Server = ConsoleMenu.AskForString();
				if (String.IsNullOrEmpty(tasks.Server))
				{
					tasks.Server = defaultDomain;
				}
				if (!String.IsNullOrEmpty(tasks.Server))
				{
					break;
				}
			}
			if (PerformAdvancedLive)
			{
				return DisplayState.AskForAdditionalUsers;
			}
			return DisplayState.Run;
		}

		DisplayState DisplayAskForAdditionalUsers()
		{
			ConsoleMenu.Title = "Indicate additional users";
			ConsoleMenu.Information = "Please specify any additional users to investigate (sAMAccountName, display name) in addition to the classic admin groups. One entry per line. End by an empty line.";
			tasks.NodesToInvestigate = ConsoleMenu.AskForListString();
			return DisplayState.Run;
		}

		DisplayState DisplayAdvancedMenu()
		{
			PerformGenerateKey = false;
			PerformHealthCheckReloadReport = false;
			PerformRegenerateReport = false;

			List<KeyValuePair<string, string>> choices = new List<KeyValuePair<string, string>>() {
				new KeyValuePair<string, string>("protocol","Change the protocol used to query the AD (LDAP, ADWS, ...)"),
				new KeyValuePair<string, string>("generatekey","Generate RSA keys used to encrypt and decrypt reports"),
				new KeyValuePair<string, string>("decrypt","Decrypt a xml report"),
				new KeyValuePair<string, string>("regenerate","Regenerate the html report based on the xml report"),
				new KeyValuePair<string, string>("log","Enable logging (log is " + (Trace.Listeners.Count > 1 ? "enabled":"disabled") + ")"),
			};

			ConsoleMenu.Title = "What do you want to do?";
			int choice = ConsoleMenu.SelectMenu(choices);
			if (choice == 0)
				return DisplayState.Exit;

			string whattodo = choices[choice - 1].Key;
			switch (whattodo)
			{
				default:
				case "protocol":
					return DisplayState.ProtocolMenu;
				case "generatekey":
					PerformGenerateKey = true;
					return DisplayState.Run;
				case "decrypt":
					PerformHealthCheckReloadReport = true;
					return DisplayState.AskForFile;
				case "regenerate":
					PerformRegenerateReport = true;
					return DisplayState.AskForFile;
				case "log":
					if (Trace.Listeners.Count <= 1)
						EnableLogFile();
					return DisplayState.Exit;
			}
		}

		DisplayState DisplayProtocolMenu()
		{
			List<KeyValuePair<string, string>> choices = new List<KeyValuePair<string, string>>() {
				new KeyValuePair<string, string>("ADWSThenLDAP","default: ADWS then if failed, LDAP"),
				new KeyValuePair<string, string>("ADWSOnly","use only ADWS"),
				new KeyValuePair<string, string>("LDAPOnly","use only LDAP"),
				new KeyValuePair<string, string>("LDAPThenADWS","LDAP then if failed, ADWS"),
			};

			ConsoleMenu.Title = "What protocol do you want to use?";
			ConsoleMenu.Information = "ADWS (Active Directory Web Service - tcp/9389) is the fastest protocol but is limited 5 sessions in parallele and a 30 minutes windows. LDAP is more stable but slower.\r\nCurrent protocol: [" + ADWebService.ConnectionType + "]";
			int defaultChoice = 1;
			for (int i = 0; i < choices.Count; i++)
			{
				if (choices[i].Key == ADWebService.ConnectionType.ToString())
					defaultChoice = 1 + i;
			}
			int choice = ConsoleMenu.SelectMenu(choices, defaultChoice);
			if (choice == 0)
				return DisplayState.Exit;

			string whattodo = choices[choice - 1].Key;
			ADWebService.ConnectionType = (ADConnectionType)Enum.Parse(typeof(ADConnectionType), whattodo);
			return DisplayState.Exit;
		}

		DisplayState DisplayAskForFile()
		{
			string file = null;
			while (String.IsNullOrEmpty(file) || !File.Exists(file))
			{
				ConsoleMenu.Title = "Select an existing report";
				ConsoleMenu.Information = "Please specify the report to open.";
				file = ConsoleMenu.AskForString();
				ConsoleMenu.Notice = "The file " + file + " was not found";
			}
			tasks.FileOrDirectory = file;
			tasks.EncryptReport = false;
			return DisplayState.Run;
		}

		// interactive interface
		private bool RunInteractiveMode()
		{
			tasks.InteractiveMode = true;
			Stack<DisplayState> states = new Stack<DisplayState>();
			var state = DisplayState.MainMenu;

			states.Push(state);
			while (states.Count > 0 && states.Peek() != DisplayState.Run)
			{
				switch (state)
				{
					case DisplayState.MainMenu:
						state = DisplayMainMenu();
						break;
					case DisplayState.ScannerMenu:
						state = DisplayScannerMenu();
						break;
					case DisplayState.AskForServer:
						state = DisplayAskServer();
						break;
					case DisplayState.AskForAdditionalUsers:
						state = DisplayAskForAdditionalUsers();
						break;
					case DisplayState.AskForScannerParameter:
						state = DisplayAskForScannerParameter();
						break;
					case DisplayState.AvancedMenu:
						state = DisplayAdvancedMenu();
						break;
					case DisplayState.AskForFile:
						state = DisplayAskForFile();
						break;
					case DisplayState.ProtocolMenu:
						state = DisplayProtocolMenu();
						break;
					default:
						// defensive programming
						if (state != DisplayState.Exit)
						{
							Console.WriteLine("No implementation of state " + state);
							state = DisplayState.Exit;
						}
						break;
				}
				if (state == DisplayState.Exit)
				{
					states.Pop();
					if (states.Count > 0)
						state = states.Peek();
				}
				else
				{
					states.Push(state);
				}
			}
			return (states.Count > 0);
		}

		private static void DisplayHelp()
		{
			Console.WriteLine("switch:");
			Console.WriteLine("  --help              : display this message");
			Console.WriteLine("  --interactive       : force the interactive mode");
			Console.WriteLine("  --log               : generate a log file");
			Console.WriteLine("  --log-console       : add log to the console");
			Console.WriteLine("");
			Console.WriteLine("Common options when connecting to the AD");
			Console.WriteLine("  --server <server>   : use this server (default: current domain controller)");
			Console.WriteLine("                        the special value * or *.forest do the healthcheck for all domains");
			Console.WriteLine("  --port <port>       : the port to use for ADWS or LDPA (default: 9389 or 389)");
			Console.WriteLine("  --user <user>       : use this user (default: integrated authentication)");
			Console.WriteLine("  --password <pass>   : use this password (default: asked on a secure prompt)");
			Console.WriteLine("  --protocol <proto>  : selection the protocol to use among LDAP or ADWS (fastest)");
			Console.WriteLine("                      : ADWSThenLDAP (default), ADWSOnly, LDAPOnly, LDAPThenADWS");
			Console.WriteLine("");
			Console.WriteLine("  --carto             : perform a quick cartography with domains surrounding");
			Console.WriteLine("");
			Console.WriteLine("  --healthcheck       : perform the healthcheck (step1)");
			Console.WriteLine("    --api-endpoint <> : upload report via api call eg: http://server");
			Console.WriteLine("    --api-key  <key>  : and using the api key as registered");
			Console.WriteLine("    --explore-trust   : on domains of a forest, after the healthcheck, do the hc on all trusted domains except domains of the forest and forest trusts");
			Console.WriteLine("    --explore-forest-trust : on root domain of a forest, after the healthcheck, do the hc on all forest trusts discovered");
			Console.WriteLine("    --explore-trust and --explore-forest-trust can be run together");
			Console.WriteLine("    --explore-exception <domains> : comma separated values of domains that will not be explored automatically");
			Console.WriteLine("");
			Console.WriteLine("    --encrypt         : use an RSA key stored in the .config file to crypt the content of the xml report");
			Console.WriteLine("    --level <level>   : specify the amount of data found in the xml file");
			Console.WriteLine("                      : level: Full, Normal, Light");
			Console.WriteLine("    --no-enum-limit   : remove the max 100 users limitation in html report");
			Console.WriteLine("    --reachable       : add reachable domains to the list of discovered domains");
			Console.WriteLine("    --sendXmlTo <emails>: send xml reports to a mailbox (comma separated email)");
			Console.WriteLine("    --sendHtmlTo <emails>: send html reports to a mailbox");
			Console.WriteLine("    --sendAllTo <emails>: send html reports to a mailbox");
			Console.WriteLine("    --notifyMail <emails>: add email notification when the mail is received");
			Console.WriteLine("    --smtplogin <user>: allow smtp credentials ...");
			Console.WriteLine("    --smtppass <pass> : ... to be entered on the command line");
			Console.WriteLine("    --smtptls         : enable TLS/SSL in SMTP if used on other port than 465 and 587");
			Console.WriteLine("    --skip-null-session: do not test for null session");
			Console.WriteLine("    --webdirectory <dir>: upload the xml report to a webdav server");
			Console.WriteLine("    --webuser <user>  : optional user and password");
			Console.WriteLine("    --webpassword <password>");
			Console.WriteLine("");
			Console.WriteLine("  --generate-key      : generate and display a new RSA key for encryption");
			Console.WriteLine("");
			Console.WriteLine("  --hc-conso          : consolidate multiple healthcheck xml reports (step2)");
			Console.WriteLine("    --center-on <domain> : center the simplified graph on this domain");
			Console.WriteLine("                         default is the domain with the most links");
			Console.WriteLine("    --xmls <path>     : specify the path containing xml (default: current directory)");
			Console.WriteLine("    --filter-date <date>: filter report generated after the date.");
			Console.WriteLine("");
			Console.WriteLine("  --regen-report <xml> : regenerate a html report based on a xml report");
			Console.WriteLine("  --reload-report <xml> : regenerate a xml report based on a xml report");
			Console.WriteLine("                          any healthcheck switches (send email, ..) can be reused");
			Console.WriteLine("    --level <level>   : specify the amount of data found in the xml file");
			Console.WriteLine("                      : level: Full, Normal, Light (default: Normal)");
			Console.WriteLine("    --encrypt         : use an RSA key stored in the .config file to crypt the content of the xml report");
			Console.WriteLine("                        the absence of this switch on an encrypted report will produce a decrypted report");
			Console.WriteLine("");
			Console.WriteLine("  --graph             : perform the light compromise graph computation directly to the AD");
			Console.WriteLine("    --encrypt         : use an RSA key stored in the .config file to crypt the content of the xml report");
			Console.WriteLine("    --max-depth       : maximum number of relation to explore (default:30)");
			Console.WriteLine("    --max-nodes       : maximum number of node to include (default:1000)");
			Console.WriteLine("    --node <node>     : create a report based on a object");
			Console.WriteLine("                      : example: \"cn=name\" or \"name\"");
			Console.WriteLine("    --nodes <file>    : create x report based on the nodes listed on a file");
			Console.WriteLine("");
			Console.WriteLine("  --scanner <type>    : perform a scan on one of all computers of the domain (using --server)");
			var scanner = PingCastleFactory.GetAllScanners();
			var scannerNames = new List<string>(scanner.Keys);
			scannerNames.Sort();
			foreach (var scannerName in scannerNames)
			{
				Type scannerType = scanner[scannerName];
				IScanner iscanner = PingCastleFactory.LoadScanner(scannerType);
				Console.ForegroundColor = ConsoleColor.Yellow;
				Console.WriteLine(iscanner.Name);
				Console.ResetColor();
				Console.WriteLine(iscanner.Description);
			}
			Console.WriteLine("  options for scanners:");
			Console.WriteLine("    --scmode-single   : force scanner to check one single computer");
			Console.WriteLine("    --nslimit <number>: Limit the number of users to enumerate (default: 5)");
			Console.WriteLine("    --foreigndomain <sid> : foreign domain targeted using its FQDN or sids");
			Console.WriteLine("                        Example of SID: S-1-5-21-4005144719-3948538632-2546531719");
			Console.WriteLine("");
			Console.WriteLine("  --upload-all-reports: use the API to upload all reports in the current directory");
			Console.WriteLine("    --api-endpoint <> : upload report via api call eg: http://server");
			Console.WriteLine("    --api-key  <key>  : and using the api key as registered");
			Console.WriteLine("                        Note: do not forget to set --level Full to send all the information available");
			Console.WriteLine("");

		}
	}


}


