﻿using BattleTech;
using BattleTech.UI;
using Harmony;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace Sheepy.AttackImprovementMod {
   using static System.Reflection.BindingFlags;

   public class Mod {

      public const string MODNAME = "AttackImprovementMod";
      public const string VERSION = "2.0 preview 20180714";
      public static ModSettings Settings = new ModSettings();

      internal static bool GameUseClusteredCallShot = false; // True if game version is less than 1.1
      internal static bool GameHitLocationBugged = false; // True if game version is less than 1.1.1
      internal static string FALLBACK_LOG_DIR = "Mods/AttackImprovementMod/";
      internal const string LOG_NAME = "Log_AttackImprovementMod.txt";
      internal static string LogDir = "";
      internal static readonly HarmonyInstance harmony = HarmonyInstance.Create( "io.github.Sheep-y.AttackImprovementMod" );
      internal static readonly Dictionary<string, ModModule> modules = new Dictionary<string, ModModule>();

      public static void Init ( string directory, string settingsJSON ) {
         LogSettings( directory, settingsJSON );

         // Hook to combat starts
         patchClass = typeof( Mod );
         Patch( typeof( CombatHUD ), "Init", typeof( CombatGameState ), null, "CombatInit" );

         modules.Add( "Logger", new AttackLog() ); // @TODO Must be above RollCorrection as long as GetCorrectedRoll is overriden
         modules.Add( "User Interface", new UserInterface() );
         modules.Add( "Line of Fire", new LineOfSight() );
         modules.Add( "Called Shot HUD", new FixCalledShotPopUp() );
         modules.Add( "Melee", new Melee() );
         modules.Add( "Roll Modifier", new RollModifier() );
         modules.Add( "Roll Corrections", new RollCorrection() );
         modules.Add( "Hit Distribution", new FixHitLocation() );

         foreach ( var mod in modules )  try {
            Log( "=== Patching " + mod.Key + " ===" );
            patchClass = mod.Value.GetType();
            mod.Value.InitPatch();
         }                 catch ( Exception ex ) { Error( ex ); }
         Log( "=== All Mod Modules Initialised ===\n" );
      }

      public static void LogSettings ( string directory, string settingsJSON ) {
         // Cache log lines until after we determined folder and deleted old log
         StringBuilder logCache = new StringBuilder()
            .AppendFormat( "========== {0} {1} ==========\r\nTime: {2}\r\nMod Folder: {3}\r\n", MODNAME, VERSION, DateTime.Now.ToString( "o" ), directory );
         try {
            Settings = JsonConvert.DeserializeObject<ModSettings>( settingsJSON );
            logCache.AppendFormat( "Mod Settings: {0}\r\n", JsonConvert.SerializeObject( Settings, Formatting.Indented ) );
         } catch ( Exception ) {
            logCache.Append( "Error: Cannot parse mod settings, using default." );
         }
         try {
            LogDir = Settings.LogFolder;
            if ( LogDir == null || LogDir.Length <= 0 )
               LogDir = directory + "/";
            logCache.AppendFormat( "Log folder set to {0}. If that fails, fallback to {1}.", LogDir, FALLBACK_LOG_DIR );
            DeleteLog( LOG_NAME );
            Log( logCache.ToString() );
         }                 catch ( Exception ex ) { Error( ex ); }

         // Detect game features. Need a proper version parsing routine. Next time.
         if ( ( VersionInfo.ProductVersion + ".0.0" ).Substring( 0, 4 ) == "1.0." ) {
            GameUseClusteredCallShot = GameHitLocationBugged = true;
            Log( "Game is 1.0.x (Clustered Called Shot, Hit Location bugged)" );
         } else if ( ( VersionInfo.ProductVersion + ".0.0." ).Substring( 0, 6 ) == "1.1.0" ) {
            GameHitLocationBugged = true;
            Log( "Game is 1.1.0 (Non-Clustered Called Shot, Hit Location bugged)" );
         } else {
            Log( "Game is 1.1.1 or up (Non-Clustered Called Shot, Hit Location fixed)" );
         }
         Log();
      }

      // ============ Harmony ============

      public static Type patchClass;
      /* Find and create a HarmonyMethod from current patchClass. method must be public and has unique name. */
      internal static HarmonyMethod MakePatch ( string method ) {
         if ( method == null ) return null;
         MethodInfo mi = patchClass.GetMethod( method );
         if ( mi == null ) {
            Error( "Cannot find patch method " + method );
            return null;
         }
         return new HarmonyMethod( mi );
      }

      internal static void Patch ( Type patchedClass, string patchedMethod, string prefix, string postfix ) {
         Patch( patchedClass, patchedMethod, Public | Instance, (Type[]) null, prefix, postfix );
      }

      internal static void Patch ( Type patchedClass, string patchedMethod, Type parameterType, string prefix, string postfix ) {
         Patch( patchedClass, patchedMethod, Public | Instance, new Type[]{ parameterType }, prefix, postfix );
      }

      internal static void Patch ( Type patchedClass, string patchedMethod, Type[] parameterTypes, string prefix, string postfix ) {
         Patch( patchedClass, patchedMethod, Public | Instance, parameterTypes, prefix, postfix );
      }

      internal static void Patch ( Type patchedClass, string patchedMethod, BindingFlags flags, string prefix, string postfix ) {
         Patch( patchedClass, patchedMethod, flags, (Type[]) null, prefix, postfix );
      }

      internal static void Patch ( Type patchedClass, string patchedMethod, BindingFlags flags, Type parameterType, string prefix, string postfix ) {
         Patch( patchedClass, patchedMethod, flags, new Type[]{ parameterType }, prefix, postfix );
      }

      internal static void Patch( Type patchedClass, string patchedMethod, BindingFlags flags, Type[] parameterTypes, string prefix, string postfix ) {
         MethodInfo patched;
         if ( ( flags & ( Static | Instance  ) ) == 0  ) flags |= Instance;
         if ( ( flags & ( Public | NonPublic ) ) == 0  ) flags |= Public;
         if ( parameterTypes == null )
            patched = patchedClass.GetMethod( patchedMethod, flags );
         else
            patched = patchedClass.GetMethod( patchedMethod, flags, null, parameterTypes, null );
         if ( patched == null ) {
            Error( "Cannot find {0}.{1}(...) to patch", patchedClass.Name, patchedMethod );
            return;
         }
         Patch( patched, prefix, postfix );
      }

      internal static void Patch( MethodInfo patched, string prefix, string postfix ) {
         if ( patched == null ) {
            Error( "Method not found. Cannot patch [ {0} : {1} ]", prefix, postfix );
            return;
         }
         HarmonyMethod pre = MakePatch( prefix ), post = MakePatch( postfix );
         if ( pre == null && post == null ) return; // MakePatch would have reported method not found
         harmony.Patch( patched, MakePatch( prefix ), MakePatch( postfix ) );
         Log( "Patched: {0} {1} [ {2} : {3} ]", patched.DeclaringType, patched, prefix, postfix );
      }

      // ============ UTILS ============

      internal static string Join<T> ( string separator, T[] array, Func<T,string> formatter = null ) {
         StringBuilder result = new StringBuilder();
         for ( int i = 0, len = array.Length ; i < len ; i++ ) {
            if ( i > 0 ) result.Append( separator );
            result.Append( formatter == null ? array[i]?.ToString() : formatter( array[i] ) );
         }
         return result.ToString();
      }

      internal static string NullIfEmpty ( ref string value ) {
         if ( value == null ) return null;
         if ( value.Trim().Length <= 0 ) return value = null;
         return value;
      }

      internal static T TryGet<T> ( ref T value, T fallback, Func<T,bool> validate = null ) {
         if ( value == null ) value = fallback;
         else if ( validate != null && ! validate( value ) ) value = fallback;
         return value;
      }

      internal static T TryGet<T> ( T[] array, int index, T fallback = default(T), string errorArrayName = null ) {
         if ( array == null || array.Length <= index ) {
            if ( errorArrayName != null ) Warn( $"{errorArrayName}[{index}] not found, using default {fallback}." );
            return fallback;
         }
         return array[ index ];
      }

      internal static V TryGet<T,V> ( Dictionary<T, V> map, T key, V fallback = default(V), string errorDictName = null ) {
         if ( map == null || ! map.ContainsKey( key ) ) {
            if ( errorDictName != null ) Warn( $"{errorDictName}[{key}] not found, using default {fallback}." );
            return fallback;
         }
         return map[ key ];
      }

      internal static void RangeCheck ( string name, ref int val, int min, int max ) {
         float v = val;
         RangeCheck( name, ref v, min, min, max, max );
         val = Mathf.RoundToInt( v );
      }

      internal static void RangeCheck ( string name, ref float val, float min, float max ) {
         RangeCheck( name, ref val, min, min, max, max );
      }

      internal static void RangeCheck ( string name, ref float val, float shownMin, float realMin, float realMax, float shownMax ) {
         if ( realMin > realMax || shownMin > shownMax ) Error( "Incorrect range check params on " + name );
         float orig = val;
         if ( val < realMin )
            val = realMin;
         else if ( val > realMax )
            val = realMax;
         if ( orig < shownMin && orig > shownMax ) {
            string message = "Warning: " + name + " must be ";
            if ( shownMin > float.MinValue )
               if ( shownMax < float.MaxValue )
                  message += " between " + shownMin + " and " + shownMax;
               else
                  message += " >= " + shownMin;
            else
               message += " <= " + shownMin;
            Log( message + ". Setting to " + val );
         }
      }

      // ============ LOGS ============

      internal static bool LogExists ( string file ) {
         return File.Exists( LogDir + file );
      }

      internal static Exception DeleteLog ( string file ) {
         Exception result = null;
         try {
            File.Delete( LogDir + file );
         } catch ( Exception e ) { result = e; }
         try {
            File.Delete( FALLBACK_LOG_DIR + file );
         } catch ( Exception e ) { result = e; }
         return result; // Example: Log( DeleteLog( name )?.ToString() ?? $"{name} deleted or not exist" );
      }

      internal static string Format ( string message, params object[] args ) {
         try {
            if ( args != null && args.Length > 0 )
               return string.Format( message, args );
         } catch ( Exception ) {}
         return message;
      }

      internal static void Log ( object message ) { Log( message.ToString() ); }
      internal static void Log ( string message, params object[] args ) { Log( Format( message, args ) ); }
      internal static void Log ( string message = "" ) { WriteLog( LOG_NAME, message + "\r\n" ); }

      internal static void Warn ( object message ) { Warn( message.ToString() ); }
      internal static void Warn ( string message ) { Log( "Warning: " + message ); }
      internal static void Warn ( string message, params object[] args ) {
         message = Format( message, args );
         HBS.Logging.Logger.GetLogger( "Mods" ).LogWarning( "[AttackImprovementMod] " + message );
         Log( "Warning: " + message );
      }

      internal static bool Error ( object message ) { 
         string txt = message.ToString();
         if ( message is Exception ) {
            if ( exceptions.ContainsKey( txt ) ) { // Increase count and don't log
               exceptions[ txt ]++;
               return true;
            } else
               exceptions.Add( txt, 1 );
         }
         Error( txt );
         return true; 
      }
      internal static void Error ( string message ) { Log( "Error: " + message ); }
      internal static void Error ( string message, params object[] args ) {
         message = Format( message, args );
         HBS.Logging.Logger.GetLogger( "Mods" ).LogError( "[AttackImprovementMod] " + message );
         Log( "Error: " + message ); 
      }

      private static Dictionary<string, int> exceptions = new Dictionary<string, int>();

      internal static void WriteLog ( string filename, string message ) {
         string logName = LogDir + filename;
         try {
            File.AppendAllText( logName, message );
         } catch ( Exception ) {
            try {
               logName = FALLBACK_LOG_DIR + filename;
               File.AppendAllText( logName, message );
            } catch ( Exception ex ) {
               Console.WriteLine( message );
               Console.Error.WriteLine( ex );
            }
         }
      }

      // ============ Game States ============

      internal static CombatHUD HUD;
      internal static CombatGameState Combat;
      internal static CombatGameConstants Constants;
      public static void CombatInit ( CombatHUD __instance ) {
         CacheCombatState();
         Mod.HUD = __instance;
         foreach ( var mod in modules ) try {
            mod.Value.CombatStarts();
         }                 catch ( Exception ex ) { Error( ex ); }
      }

      public static void CacheCombatState () {
         Combat = UnityGameInstance.BattleTechGame?.Combat;
         Constants = Combat?.Constants;
      }
   }

   public abstract class ModModule {
      public abstract void InitPatch();
      public virtual void CombatStarts () { }
   }
}