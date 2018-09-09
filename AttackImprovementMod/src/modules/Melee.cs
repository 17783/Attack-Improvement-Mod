﻿using BattleTech.UI;
using BattleTech;
using Harmony;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sheepy.BattleTechMod.AttackImprovementMod {
   using static Mod;
   using static System.Reflection.BindingFlags;

   public class Melee : BattleModModule {

      public override void CombatStartsOnce () {
         Type PathingType = typeof( Pathing );
         if ( Settings.UnlockMeleePositioning && BattleMod.FoundMod( "de.morphyum.MeleeMover", "MeleeMover.MeleeMover" ) ) {
            BattleMod.BTML_LOG.Warn( Mod.Name + " detected morphyum's MeleeMover, melee positioning unlock left in MeleeMover's hands." );
            Settings.UnlockMeleePositioning = false;
         }
         if ( Settings.UnlockMeleePositioning )
            Patch( PathingType, "GetMeleeDestsForTarget", typeof( AbstractActor ), null, null, "UnlockMeleeDests" );

         if ( Settings.MaxMeleeVerticalOffsetByClass != null )
            if ( InitMaxVerticalOffset() ) {
               Patch( PathingType, "GetMeleeDestsForTarget", "SetMeleeTarget", "ClearMeleeTarget" );
               Patch( PathingType, "GetPathNodesForPoints", null, "CheckMeleeVerticalOffset" );
            }
      }

      public override void CombatStarts () {
         if ( Settings.IncreaseMeleePositionChoice || Settings.IncreaseDFAPositionChoice || MaxMeleeVerticalOffsetByClass != null ) {
            MovementConstants con = CombatConstants.MoveConstants;
            if ( Settings.IncreaseMeleePositionChoice )
               con.NumMeleeDestinationChoices = 6;
            if ( Settings.IncreaseDFAPositionChoice )
               con.NumDFADestinationChoices = 6;
            if ( MaxMeleeVerticalOffsetByClass != null )
               con.MaxMeleeVerticalOffset = 1000;
            typeof( CombatGameConstants ).GetProperty( "MoveConstants" ).SetValue( CombatConstants, con, null );
         }
      }

      public static IEnumerable<CodeInstruction> UnlockMeleeDests ( IEnumerable<CodeInstruction> input ) {
         return ReplaceIL( input,
            ( code ) => code.opcode.Name == "ldc.r4" && code.operand != null && code.operand.Equals( 10f ),
            ( code ) => { code.operand = 0f; return code; },
            1, "UnlockMeleePositioning", ModLog
            );
      }

      // ============ Vertical Offset ============

      private static float[] MaxMeleeVerticalOffsetByClass;

      private bool InitMaxVerticalOffset () {
         MaxMeleeVerticalOffsetByClass = null;
         List<float> list = new List<float>();
         foreach ( string e in Settings.MaxMeleeVerticalOffsetByClass.Split( ',' ) ) try {
            if ( list.Count >= 4 ) break;
            float offset = float.Parse( e.Trim() );
            if ( offset < 0 || float.IsNaN( offset ) || float.IsInfinity( offset ) ) throw new ArgumentOutOfRangeException();
            list.Add( offset );
         } catch ( Exception ex ) {
            Warn( "Can't parse \'{0}\' in MaxMeleeVerticalOffsetByClass as a positive number: {1}", e, ex );
            list.Add( list.Count > 0 ? list[ list.Count-1 ] : 8 );
         }
         if ( list.Count <= 0 ) return false;
         while ( list.Count < 4 )
            list.Add( list[ list.Count-1 ] );
         if ( ! list.Exists( e => e != 4 ) ) return false;
         MaxMeleeVerticalOffsetByClass = list.ToArray();
         return true;
      }

      private static AbstractActor thisMeleeAttacker, thisMeleeTarget;

      [ HarmonyPriority( Priority.High ) ]
      public static void SetMeleeTarget ( Pathing __instance, AbstractActor target ) {
         thisMeleeAttacker = __instance.OwningActor;
         thisMeleeTarget = target;
      }

      public static void ClearMeleeTarget () {
         thisMeleeTarget = null;
      }

      // Set the game's MaxMeleeVerticalOffset to very high, then filter nodes at GetPathNodesForPoints
      public static void CheckMeleeVerticalOffset ( List<PathNode> __result ) {
         if ( thisMeleeTarget == null ) return;
         float targetY = thisMeleeTarget.CurrentPosition.y, maxY = 0;
         WeightClass lowerClass = 0;
			for (int i = __result.Count ; i >= 0 ; i-- ) {
            float attackerY = __result[ i ].Position.y;
            if ( attackerY > targetY )
               lowerClass = thisMeleeTarget is Mech mech ? mech.weightClass : WeightClass.LIGHT;
            else if ( targetY > attackerY )
               lowerClass = thisMeleeAttacker is Mech mech ? mech.weightClass : WeightClass.LIGHT;
            else
               continue;
            switch ( lowerClass ) {
               case WeightClass.LIGHT  : maxY = MaxMeleeVerticalOffsetByClass[0]; break;
               case WeightClass.MEDIUM : maxY = MaxMeleeVerticalOffsetByClass[1]; break;
               case WeightClass.HEAVY  : maxY = MaxMeleeVerticalOffsetByClass[2]; break;
               case WeightClass.ASSAULT: maxY = MaxMeleeVerticalOffsetByClass[3]; break;
            }
            if ( Math.Abs( attackerY - targetY ) > maxY )
               __result.RemoveAt( i );
         }
      }
   }
}