using System;
using System.Reflection;

namespace Sheepy.AttackImprovementMod {
   using static Mod;
   using static System.Reflection.BindingFlags;
   using BattleTech.UI;
   using System.Collections.Generic;
   using BattleTech;

   public class AttackAction : ModModule {

      public override void InitPatch () {
         if ( Mod.Settings.FixMultiTargetBackout ) {
            if ( targetedCombatant == null )
               Warn( "Cannot find SelectionState.targetedCombatant. MultiTarget backup may triggers target lock sound effect." );
            if ( ClearTargetedActor == null )
               Warn( "Cannot find SelectionStateFireMulti.ClearTargetedActor. MultiTarget backout may be slightly inconsistent." );
            if ( RemoveTargetedCombatant == null )
               Error( "Cannot find RemoveTargetedCombatant(), SelectionStateFireMulti not patched" );
            else if ( weaponTargetIndices == null )
               Error( "Cannot find weaponTargetIndices, SelectionStateFireMulti not patched" );
            else {
               Patch( typeof( CombatSelectionHandler ), "BackOutOneStep", NonPublic, null, "PreventMultiTargetBackout" );
               Patch( typeof( SelectionStateFireMulti ), "get_CanBackOut", "OverrideMultiTargetCanBackout", null );
               Patch( typeof( SelectionStateFireMulti ), "BackOut", "OverrideMultiTargetBackout", null );
               Patch( typeof( SelectionStateFireMulti ), "RemoveTargetedCombatant", NonPublic, "OverrideRemoveTargetedCombatant", null );
            }
         }
      }

      // ============ Fixes ============

      private static bool ReAddStateData = false;

      public static void PreventMultiTargetBackout ( CombatSelectionHandler __instance ) {
         if ( ReAddStateData ) {
            // Re-add self state onto selection stack to prevent next backout from cancelling command
            __instance.NotifyChange( CombatSelectionHandler.SelectionChange.StateData );
         }
      }

      public static bool OverrideMultiTargetCanBackout ( SelectionStateFireMulti __instance, ref bool __result ) {
         __result = __instance.Orders == null && __instance.AllTargetedCombatantsCount > 0;
         return false;
      }

      private static FieldInfo targetedCombatant = typeof( SelectionState ).GetField( "targetedCombatant", NonPublic | Instance );
      private static PropertyInfo weaponTargetIndices = typeof( SelectionStateFireMulti ).GetProperty( "weaponTargetIndices", NonPublic | Instance );
      private static MethodInfo RemoveTargetedCombatant = typeof( SelectionStateFireMulti ).GetMethod( "RemoveTargetedCombatant", NonPublic | Instance );
      private static MethodInfo ClearTargetedActor = typeof( SelectionStateFireMulti ).GetMethod( "ClearTargetedActor", NonPublic | Instance | FlattenHierarchy );
      private static object[] RemoveTargetParams = new object[]{ null, false };

      public static bool OverrideMultiTargetBackout ( SelectionStateFireMulti __instance ) { try {
         var me = __instance;
         var allTargets = me.AllTargetedCombatants;
         int count = allTargets.Count;
         if ( count > 0 ) {
            // Change target to second newest to reset keyboard focus and thus dim cancelled target's LOS
            ICombatant newTarget = count > 1 ? allTargets[ count - 2 ] : null;
            HUD.SelectionHandler.TrySelectTarget( newTarget );
            // Try one of the reflection ways to set new target
            if ( newTarget == null && ClearTargetedActor != null )
               ClearTargetedActor.Invoke( me, null ); // Hide fire button
            else if ( targetedCombatant != null )
               targetedCombatant.SetValue( me, newTarget ); // Skip soft lock sound
            else
               me.SetTargetedCombatant( newTarget );
            // The only line that is same as old!
            RemoveTargetedCombatant.Invoke( me, RemoveTargetParams );
            // Amend selection state later
            ReAddStateData = true;
            return false;
         }
         return true;
      }                 catch ( Exception ex ) { return Error( ex ); } }

      public static bool OverrideRemoveTargetedCombatant ( SelectionStateFireMulti __instance, ICombatant target, bool clearedForFiring ) { try {
         var allTargets = __instance.AllTargetedCombatants;
         int index = target == null ? allTargets.Count - 1 : allTargets.IndexOf( target );
         if ( index < 0 ) return false;

         // Fix weaponTargetIndices
         var indice = (Dictionary<Weapon, int>) weaponTargetIndices.GetValue( __instance, null );
         Weapon[] weapons = new Weapon[ indice.Count ];
         indice.Keys.CopyTo( weapons, 0 );
         foreach ( var weapon in weapons ) {
            if ( indice[ weapon ] > index ) 
               indice[ weapon ] -= - 1;
            else if ( indice[ weapon ] == index )
               indice[ weapon ] = -1;
         }
         // End Fix

         allTargets.RemoveAt( index );
         Combat.MessageCenter.PublishMessage( new ActorMultiTargetClearedMessage( index.ToString(), clearedForFiring ) );
         return false;
      }                 catch ( Exception ex ) { return Error( ex ); } }
   }
}