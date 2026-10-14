using System;
class ProgressFixture{static void Main(){Console.In.ReadToEnd();Console.Error.WriteLine("ARCCACHE_PROGRESS invalid");Console.Error.WriteLine("ARCCACHE_PROGRESS {\"Percent\":120,\"Message\":\"First step\"}");System.Threading.Thread.Sleep(1500);Console.Write("{\"Ok\":true,\"Message\":\"Done\",\"Data\":{}}");}}
