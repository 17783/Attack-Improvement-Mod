﻿using BattleTech.UI;
using BattleTech;
using Harmony;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using static System.Reflection.BindingFlags;

namespace Sheepy.BattleTechMod.AttackImprovementMod {
   using static Mod;

   public class LineOfSight : BattleModModule {

      public override void CombatStartsOnce () {
         if ( Settings.FacingMarkerPlayerColors != null || Settings.FacingMarkerEnemyColors != null || Settings.FacingMarkerTargetColors != null ) {
            SetColors = typeof( AttackDirectionIndicator ).GetMethod( "SetColors", NonPublic | Instance );
            if ( SetColors == null ) {
               Warn( "Cannot find AttackDirectionIndicator.SetColors, direction marker colors not patched." );
            } else {
               TryRun( ModLog, InitDirectionColors );
               Patch( typeof( AttackDirectionIndicator ), "ShowAttackDirection", "SaveDirectionMarker", "SetDirectionMarker" );
            }
         }

         TryRun( ModLog, InitSettings );
         bool LinesChanged = Settings.LOSIndirectDotted || parsedColor.ContainsKey( Line.Indirect ) ||
                                Settings.LOSMeleeDotted || parsedColor.ContainsKey( Line.Melee ) ||
                                Settings.LOSClearDotted || parsedColor.ContainsKey( Line.Clear ) ||
                           Settings.LOSBlockedPreDotted || parsedColor.ContainsKey( Line.BlockedPre ) ||
                          Settings.LOSBlockedPostDotted || parsedColor.ContainsKey( Line.BlockedPost ) ||
                           ! Settings.LOSNoAttackDotted || parsedColor.ContainsKey( Line.NoAttack );
         Type Indicator = typeof( WeaponRangeIndicators );

         if ( Settings.LOSWidth != 1 || Settings.LOSWidthBlocked != 0.75m || Settings.LOSMarkerBlockedMultiplier != 1 )
            Patch( Indicator, "Init", null, "ResizeLOS" );

         if ( LinesChanged ) {
            Patch( Indicator, "Init", null, "CreateLOSTexture" );
            Patch( Indicator, "DrawLine", "SetupLOS", "CleanupLOS" );
            Patch( Indicator, "ShowLineToTarget", null, "ShowBlockedLOS" );
         }
         if ( LinesChanged || Settings.ArcLinePoints != 18 )
            Patch( Indicator, "getLine" , null, "FixLOSWidth" );

         if ( Settings.ArcLinePoints != 18 ) {
            Patch( Indicator, "DrawLine", null, null, "ModifyArcPoints" );
            Patch( typeof( CombatPathLine ), "DrawJumpPath", null, null, "ModifyArcPoints" );
         }
      }

      // ============ Marker Colour ============

      private static MethodInfo SetColors;
      private static Color[] OrigDirectionMarkerColors; // [ Active #FFFFFF4B, Inactive #F8441464 ]
      private static Color?[] FacingMarkerPlayerColors, FacingMarkerEnemyColors, FacingMarkerTargetColors;

      private static void InitDirectionColors () {
         FacingMarkerPlayerColors = new Color?[ LOSDirectionCount ];
         FacingMarkerEnemyColors  = new Color?[ LOSDirectionCount ];
         FacingMarkerTargetColors = new Color?[ LOSDirectionCount ];

         string[] player = Settings.FacingMarkerPlayerColors?.Split( ',' ).Select( e => e.Trim() ).ToArray();
         string[] enemy  = Settings.FacingMarkerEnemyColors ?.Split( ',' ).Select( e => e.Trim() ).ToArray();
         string[] active = Settings.FacingMarkerTargetColors?.Split( ',' ).Select( e => e.Trim() ).ToArray();

         for ( int i = 0 ; i < LOSDirectionCount ; i++ ) {
            if ( player != null && player.Length > i )
               FacingMarkerPlayerColors[ i ] = ParseColour( player[ i ] );
            if ( enemy  != null && enemy.Length > i )
               FacingMarkerEnemyColors [ i ] = ParseColour( enemy [ i ] );
            if ( active != null && active.Length > i )
               FacingMarkerTargetColors[ i ] = ParseColour( active[ i ] );
         }
         Info( "Player direction marker = {0}", FacingMarkerPlayerColors.Select( e => ColorUtility.ToHtmlStringRGBA( e.GetValueOrDefault() ) ) );
         Info( "Enemy  direction marker = {0}", FacingMarkerEnemyColors .Select( e => ColorUtility.ToHtmlStringRGBA( e.GetValueOrDefault() ) ) );
         Info( "Target direction marker = {0}", FacingMarkerTargetColors.Select( e => ColorUtility.ToHtmlStringRGBA( e.GetValueOrDefault() ) ) );
      }

      public static void SaveDirectionMarker ( AttackDirectionIndicator __instance ) {
         if ( OrigDirectionMarkerColors == null )
            OrigDirectionMarkerColors = new Color[]{ __instance.ColorInactive, __instance.ColorActive };
      }

      public static void SetDirectionMarker ( AttackDirectionIndicator __instance, AttackDirection direction ) { try {
         AttackDirectionIndicator me =  __instance;
			if ( me.Owner.IsDead ) return;
         Color orig = me.ColorInactive;
         Color?[] activeColors = __instance.Owner?.team?.IsFriendly( Combat.LocalPlayerTeam ) ?? false ? FacingMarkerPlayerColors : FacingMarkerEnemyColors;
         object[] colors;
         if ( direction != AttackDirection.ToProne && direction != AttackDirection.FromTop ) {
            colors = new object[]{ activeColors?[0] ?? orig, activeColors?[1] ?? orig, activeColors?[2] ?? orig, activeColors?[3] ?? orig };
            if ( direction != AttackDirection.None ) {
               int dirIndex = Math.Max( 0, Math.Min( (int) direction - 1, LOSDirectionCount-1 ) );
               colors[ dirIndex ] = FacingMarkerTargetColors?[ dirIndex ] ?? me.ColorActive;
               //Log( $"Direction {direction}, Index {dirIndex}, Color {colors[ dirIndex ]}" );
            }
         } else {
            if ( ActiveState == null ) return;
            FiringPreviewManager.PreviewInfo info = ActiveState.FiringPreview.GetPreviewInfo( me.Owner );
            orig = info.HasLOF ? ( FacingMarkerTargetColors[4] ?? me.ColorActive ) : ( activeColors[4] ?? me.ColorInactive );
            colors = new object[]{ orig, orig, orig, orig };
         }
         SetColors.Invoke( __instance, colors );
      }                 catch ( Exception ex ) { Error( ex ); } }

      // ============ Line change ============

      private static bool losTextureScaled = false;

      public static void ResizeLOS ( WeaponRangeIndicators __instance ) { try {
         WeaponRangeIndicators me = __instance;

         float width = (float) Settings.LOSWidth;
         if ( width > 0 && me.LOSWidthBegin != width ) {
            //Log( "Setting default LOS width to {0}", width );
            // Scale solid line width
            me.LOSWidthBegin = width;
            me.LOSWidthEnd = width;
            // Scale Out of Range line width, when line is solid
            me.LineTemplate.startWidth = width;
            me.LineTemplate.endWidth = width;
            // Scale all dotted lines
            if ( ! losTextureScaled ) {
               Vector2 s = me.MaterialOutOfRange.mainTextureScale;
               s.x /= width;
               me.MaterialOutOfRange.mainTextureScale = s;
            }
         }

         width = (float) Settings.LOSWidthBlocked;
         if ( width > 0 && me.LOSWidthBlocked != width )
            me.LOSWidthBlocked = width;
         //Log( "LOS widths, normal = {0}, post-blocked = {1}", me.LOSWidthBegin, me.LOSWidthBlocked );

         width = (float) Settings.LOSMarkerBlockedMultiplier;
         if ( width != 1 && ! losTextureScaled ) {
            //Log( "Scaling LOS block marker by {0}", width );
            Vector3 zoom = me.CoverTemplate.transform.localScale;
            zoom.x *= width;
            zoom.y *= width;
            me.CoverTemplate.transform.localScale = zoom;
         }
         losTextureScaled = true;
      }                 catch ( Exception ex ) { Error( ex ); } }

      private const int Melee = 0, Clear = 1, BlockedPre = 2, BlockedPost = 3, Indirect = 4, NoAttack = 5;
      internal enum Line { Melee = 0, Clear = 1, BlockedPre = 2, BlockedPost = 3, Indirect = 4, NoAttack = 5 }

      // Original materials and colours
      private static Material Solid, OrigInRangeMat, Dotted, OrigOutOfRangeMat;
      private static Color[] OrigColours;

      // Modded materials
      private static Dictionary<Line,Color?[]> parsedColor; // Exists until Mats are created. Each row and colour may be null.
      private static LosMaterial[][] Mats; // Replaces parsedColor. Either whole row is null or whole row is filled.
      internal const int LOSDirectionCount = 5;

      private static void InitSettings () {
         parsedColor = new Dictionary<Line, Color?[]>( LOSDirectionCount );
         foreach ( Line line in (Line[]) Enum.GetValues( typeof( Line ) ) ) {
            FieldInfo colorsField = typeof( ModSettings ).GetField( "LOS" + line + "Colors"  );
            string colorTxt = colorsField.GetValue( Settings )?.ToString();
            List<string> colorList = colorTxt?.Split( ',' ).Select( e => e.Trim() ).ToList();
            if ( colorList == null ) continue;
            Color?[] colors = new Color?[ LOSDirectionCount ];
            for ( int i = 0 ; i < LOSDirectionCount ; i++ )
               if ( colorList.Count > i )
                  colors[ i ] = ParseColour( colorList[ i ] );
            if ( colors.Any( e => e != null ) )
               parsedColor.Add( line, colors );
         }
      }

      public static void CreateLOSTexture ( WeaponRangeIndicators __instance ) { try {
         WeaponRangeIndicators me = __instance;
         if ( parsedColor != null ) {
            Solid = OrigInRangeMat = me.MaterialInRange;
            Dotted = OrigOutOfRangeMat = me.MaterialOutOfRange;
            OrigColours = new Color[]{ me.LOSInRange, me.LOSOutOfRange, me.LOSUnlockedTarget, me.LOSLockedTarget, me.LOSMultiTargetKBSelection, me.LOSBlocked };

            Mats = new LosMaterial[ NoAttack + 1 ][];
            foreach ( Line line in (Line[]) Enum.GetValues( typeof( Line ) ) )
               Mats[ (int) line ] = NewMat( line );

            // Make sure post mat is applied even if pre mat was not modified
            if ( Mats[ BlockedPost ] != null && Mats[ BlockedPre ] == null ) {
               Mats[ BlockedPre ] = new LosMaterial[ LOSDirectionCount ];
               for ( int i = 0 ; i < LOSDirectionCount ; i++ )
                  Mats[ BlockedPre ][i] = new LosMaterial( OrigColours[5], false, (float) Settings.LOSWidthBlocked, "BlockedPreLOS"+i );
            }
            parsedColor = null;
         }
      } catch ( Exception ex ) {
         Mats = new LosMaterial[ NoAttack + 1 ][]; // Reset all materials
         Error( ex );
      } }

      private static bool RestoreMat = false;
      private static LineRenderer thisLine;

      public static void FixLOSWidth ( LineRenderer __result, WeaponRangeIndicators __instance ) {
         thisLine = __result;
         // Reset line width to default to prevent blocked width from leaking to no attack width.
         thisLine.startWidth = __instance.LOSWidthBegin;
         thisLine.endWidth = __instance.LOSWidthEnd;
      }

      private static int lastDirIndex;

      public static void SetupLOS ( WeaponRangeIndicators __instance, Vector3 position, AbstractActor selectedActor, ICombatant target, bool usingMultifire, bool isMelee ) { try {
         WeaponRangeIndicators me = __instance;
         int dirIndex = 0;
         if ( target is Mech || target is Vehicle ) {
            bool canSee = selectedActor.HasLOSToTargetUnit( target );
            dirIndex = canSee ? Math.Max( 0, Math.Min( (int) Combat.HitLocation.GetAttackDirection( position, target ) - 1, LOSDirectionCount-1 ) ) : 0;
         }
         if ( dirIndex != lastDirIndex && Mats[ NoAttack ] != null ) {
            me.MaterialOutOfRange = Mats[ NoAttack ][ dirIndex ].GetMaterial();
            me.LOSOutOfRange = Mats[ NoAttack ][ dirIndex ].GetColor();
         }
         lastDirIndex = dirIndex;
         if ( isMelee )
            SwapMat( me, Melee, dirIndex, ref me.LOSLockedTarget, false );
         else {
            FiringPreviewManager.PreviewInfo info = ActiveState.FiringPreview.GetPreviewInfo( target );
            if ( info.HasLOF )
               if ( info.LOFLevel == LineOfFireLevel.LOFClear )
                  SwapMat( me, Clear, dirIndex, ref me.LOSInRange, usingMultifire );
               else {
                  if ( SwapMat( me, BlockedPre, dirIndex, ref me.LOSInRange, usingMultifire ) )
                     me.LOSBlocked = Mats[ BlockedPre ][ dirIndex ].GetColor();
               }
            else
               SwapMat( me, Indirect, dirIndex, ref me.LOSInRange, usingMultifire );
         }
      }                 catch ( Exception ex ) { Error( ex ); } }

      public static void CleanupLOS ( WeaponRangeIndicators __instance, bool usingMultifire ) {
         //Log( "Mat = {0}, Width = {1}, Color = {2}", thisLine.material.name, thisLine.startWidth, thisLine.startColor );
         if ( thisLine.material.name.StartsWith( "BlockedPreLOS" ) ) {
            thisLine.material = Mats[ BlockedPost ][ lastDirIndex ].GetMaterial();
            thisLine.startColor = thisLine.endColor = Mats[ BlockedPost ][ lastDirIndex ].GetColor();
            //Log( "Swap to blocked post {0}, Width = {1}, Color = {2}", thisLine.material.name, thisLine.startWidth, thisLine.startColor );
         }
         if ( RestoreMat ) {
            WeaponRangeIndicators me = __instance;
            me.MaterialInRange = OrigInRangeMat;
            me.LOSInRange = OrigColours[0];
            if ( usingMultifire ) {
               me.LOSUnlockedTarget = OrigColours[2];
               me.LOSLockedTarget = OrigColours[3];
               me.LOSMultiTargetKBSelection = OrigColours[4];
            }
            me.LOSBlocked = OrigColours[5];
            RestoreMat = false;
         }
      }

      // Make sure Blocked LOS is displayed in single target mode.
      public static void ShowBlockedLOS () {
         thisLine?.gameObject?.SetActive( true );
      }

      // ============ Utils ============

      private static LosMaterial[] NewMat ( Line line ) {
         string name = line.ToString();
         parsedColor.TryGetValue( line, out Color?[] colors );
         bool dotted  = (bool) typeof( ModSettings ).GetField( "LOS" + name + "Dotted" ).GetValue( Settings );
         if ( colors == null ) {
            if ( dotted == ( name.StartsWith( "NoAttack" ) ) ) return null;
            colors = new Color?[ LOSDirectionCount ];
         }
         //Log( "NewMat " + line + " = " + Join( ",", colors ) );
         LosMaterial[] lineMats = new LosMaterial[ LOSDirectionCount ];
         for ( int i = 0 ; i < LOSDirectionCount ; i++ )
            lineMats[ i ] = NewMat( name + "LOS" + i, name.StartsWith( "NoAttack" ), colors[i], i > 0 ? colors[i-1] : null, dotted );
         return lineMats;
      }

      private static LosMaterial NewMat ( string name, bool origInRange, Color? color, Color? fallback, bool dotted ) { try {
         return new LosMaterial( color ?? fallback ?? OrigColours[ origInRange ? 0 : 1 ], dotted, (float) Settings.LOSWidth, name );
      }                 catch ( Exception ex ) { Error( ex ); return null; } }

      private static bool SwapMat ( WeaponRangeIndicators __instance, int matIndex, int dirIndex, ref Color lineColor, bool IsMultifire ) {
         Material newMat = Mats[ matIndex ]?[ dirIndex ].GetMaterial();
         if ( newMat == null ) return false;
         WeaponRangeIndicators me = __instance;
         me.MaterialInRange = newMat;
         lineColor = newMat.color;
         if ( IsMultifire ) {
            me.LOSUnlockedTarget = me.LOSLockedTarget = me.LOSMultiTargetKBSelection = lineColor;
            me.LOSUnlockedTarget.a *= 0.8f;
         }
         //Verbo( $"Swapped to {matIndex} {dirIndex} {newMat.name}" );
         return RestoreMat = true;
      }

      // ============ Arcs ============

      public static IEnumerable<CodeInstruction> ModifyArcPoints ( IEnumerable<CodeInstruction> input ) {
         return ReplaceIL( input,
            ( code ) => code.opcode.Name == "ldc.i4.s" && code.operand != null && code.operand.Equals( (sbyte) 18 ),
            ( code ) => { code.operand = (sbyte) Settings.ArcLinePoints; return code; },
            2, "SetIndirectSegments", ModLog );
      }

      /*
      LOSInRange = RGBA(1.000, 0.157, 0.157, 1.000) #FF2828FF
      LOSOutOfRange = RGBA(1.000, 1.000, 1.000, 0.275) #FFFFFF46
      LOSUnlockedTarget = RGBA(0.757, 0.004, 0.004, 0.666) #C00000AA
      LOSLockedTarget = RGBA(0.853, 0.004, 0.004, 1.000) #DA0000FF
      LOSMultiTargetKBSelection = RGBA(1.000, 0.322, 0.128, 1.000) #FF5221FF
      LOSBlocked = RGBA(0.853, 0.000, 0.000, 0.753) #DA0000C0
      LOSWidthBegin = 1
      LOSWidthEnd = 0.75
      LOSWidthBlocked = 0.4
      LOSWidthFacingTargetMultiplier = 2.5f
      */

      public class LosMaterial {
         private readonly Material Material;
         private readonly Color Color;
         public readonly float Width;
         public LosMaterial ( Color color, bool dotted, float width, string name ) {
            Color = color;
            Width = width;
            Material = new Material( dotted ? Dotted : Solid ) { name = name, color = Color };
            if ( dotted && width != 1 ) {
               Vector2 s = Material.mainTextureScale;
               s.x *= 1 / width;
               Material.mainTextureScale = s;
            }
         }
         public Material GetMaterial () {
            return Material;
         }
         public Color GetColor () {
            return Color;
         }
      }
   }
}