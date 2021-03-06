﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Xml.Linq;
using EloBuddy;
using EloBuddy.SDK;
using EloBuddy.SDK.Enumerations;
using EloBuddy.SDK.Events;
using EloBuddy.SDK.Rendering;
using SharpDX;
using Color = System.Drawing.Color;

namespace SimpleAhri
{
    public static class Program
    {
        public const string ChampName = "Ahri";

        public static AIHeroClient CurrentTarget;

        public static readonly List<ProcessSpellCastCache> CachedAntiGapclosers = new List<ProcessSpellCastCache>();
        public static readonly List<ProcessSpellCastCache> CachedInterruptibleSpells = new List<ProcessSpellCastCache>();

        private static Vector3 _flagPos;
        private static int _flagCreateTick;

        public static Text[] InfoText { get; set; }

        public static Item Rabadon;

        public static Spell.Targeted Ignite;

        public static MissileClient QOrbMissile, QReturnMissile;

        public static void Main(string[] args)
        {
            Loading.OnLoadingComplete += OnLoadingComplete;
        }

        private static void OnLoadingComplete(EventArgs args)
        {
            if (Player.Instance.ChampionName != ChampName)
            {
                return;
            }

            Config.Initialize();
            SpellManager.Initialize();
            ModeManager.Initialize();

            Drawing.OnDraw += OnDraw;
            Drawing.OnEndScene += Drawing_OnEndScene;
            Obj_AI_Base.OnProcessSpellCast += Obj_AI_Base_OnProcessSpellCast;
            Game.OnTick += Game_OnTick;
            GameObject.OnCreate += GameObject_OnCreate;
            GameObject.OnDelete += GameObject_OnDelete;
            HPBarIndicator.Initalize();

            if (Config.Drawings.DrawPermashow)
            {
                PermaShow.Initalize();
            }

            InfoText = new Text[3];
            InfoText[0] = new Text("", new Font("calibri", 18, FontStyle.Regular));

            var ignite = Player.Spells.FirstOrDefault(s => s.Name.ToLower().Contains("summonerdot"));
            if (ignite != null)
            {
                Ignite = new Spell.Targeted(ignite.Slot, 600);
            }

            if (Config.MiscMenu.SkinHackEnabled)
            {
                Player.SetSkin(Player.Instance.BaseSkinName, Config.MiscMenu.SkinId);
            }

            Rabadon = new Item(3089);

            Helpers.PrintInfoMessage("Addon loaded !");
        }

        private static void Drawing_OnEndScene(EventArgs args)
        {
            if (!Config.Drawings.Enabled)
                return;

            if (Config.Drawings.DrawRTime && SpellManager.R.IsLearned)
            {
                var rbuff = Player.Instance.GetBuff("AhriTumble");

                if (rbuff != null)
                {
                    var percentage = 100 * Math.Max(0, rbuff.EndTime - Game.Time) / 10;

                    var g = Math.Max(0, 255f / 100f * percentage);
                    var r = Math.Max(0, 255 - g);

                    var color = Color.FromArgb((int)r, (int)g, 0);

                    InfoText[0].Color = color;
                    InfoText[0].X = (int)Drawing.WorldToScreen(Player.Instance.Position).X;
                    InfoText[0].Y = (int)Drawing.WorldToScreen(Player.Instance.Position).Y;
                    InfoText[0].TextValue = "\n\nR expiry time : " + Math.Max(0, rbuff.EndTime - Game.Time).ToString("F1") + "s | Stacks : "+ Player.Instance.GetBuff("AhriTumble").Count;
                    InfoText[0].Draw();
                }
            }
        }

        private static void Game_OnTick(EventArgs args)
        {
            if (_flagCreateTick != 0 && _flagCreateTick + 8500 < Game.Time * 1000)
            {
                _flagCreateTick = 0;
                _flagPos = Vector3.Zero;
            }

            CurrentTarget = TargetSelector.GetTarget(SpellManager.E.Range, DamageType.Magical);

            CachedAntiGapclosers.RemoveAll(x => Game.Time * 1000 > x.Tick + 850);
            CachedInterruptibleSpells.RemoveAll(x => Game.Time * 1000 > x.Tick + 8000);

            if (Config.InterrupterMenu.Enabled)
            {
                var processSpellCastCache = CachedInterruptibleSpells.FirstOrDefault();

                if (processSpellCastCache != null)
                {
                    var enemy = processSpellCastCache.Sender;

                    if (!enemy.Spellbook.IsCastingSpell && !enemy.Spellbook.IsCharging && !enemy.Spellbook.IsChanneling)
                        CachedInterruptibleSpells.Remove(processSpellCastCache);
                }
            }
        }

        private static void Obj_AI_Base_OnProcessSpellCast(Obj_AI_Base sender, GameObjectProcessSpellCastEventArgs args)
        {
            if (sender.IsMe)
                return;


            var enemy = sender as AIHeroClient;

            if (enemy == null)
                return;

            if (Config.GapcloserMenu.Enabled && Config.GapcloserMenu.GapclosersFound != 0)
            {
                var menudata = Config.AntiGapcloserMenuValues.FirstOrDefault(x => x.Champion == enemy.ChampionName);

                if (menudata == null)
                    return;

                if (enemy.Hero == Champion.Nidalee || enemy.Hero == Champion.Tristana || enemy.Hero == Champion.JarvanIV)
                {
                    if (enemy.Hero == Champion.JarvanIV && menudata.Enabled &&
                        args.SData.Name.ToLower() == "jarvanivdemacianstandard" &&
                        args.End.Distance(Player.Instance.Position) < 1000)
                    {
                        _flagPos.X = args.End.X;
                        _flagPos.Y = args.End.Y;
                        _flagPos.Z = NavMesh.GetHeightForPosition(args.End.X, args.End.Y);
                        _flagCreateTick = (int)Game.Time * 1000;
                    }

                    if (enemy.Hero == Champion.Nidalee && menudata.Enabled && args.SData.Name.ToLower() == "pounce" &&
                        args.End.Distance(Player.Instance.Position) < 350)
                    {
                        CachedAntiGapclosers.Add(new ProcessSpellCastCache
                        {
                            Sender = enemy,
                            NetworkId = enemy.NetworkId,
                            DangerLevel = menudata.DangerLevel,
                            Tick = (int)Game.Time * 1000
                        });
                    }

                    if (enemy.Hero == Champion.JarvanIV && menudata.Enabled &&
                        args.SData.Name.ToLower() == "jarvanivdragonstrike" &&
                        args.End.Distance(Player.Instance.Position) < 1000)
                    {
                        var flagpolygon = new Geometry.Polygon.Circle(_flagPos, 150);
                        var playerpolygon = new Geometry.Polygon.Circle(Player.Instance.Position, 150);

                        for (var i = 0; i < 1000; i += 25)
                        {
                            if (flagpolygon.IsInside(enemy.Position.Extend(args.End, i)) && playerpolygon.IsInside(enemy.ServerPosition.Extend(args.End, i)))
                            {
                                CachedAntiGapclosers.Add(new ProcessSpellCastCache
                                {
                                    Sender = enemy,
                                    NetworkId = enemy.NetworkId,
                                    DangerLevel = menudata.DangerLevel,
                                    Tick = (int)Game.Time * 1000
                                });
                                break;
                            }
                        }
                    }
                }
                else
                {
                    if (menudata.Enabled && args.Slot == menudata.SpellSlot &&
                        args.End.Distance(Player.Instance.Position) < 350)
                    {
                        CachedAntiGapclosers.Add(new ProcessSpellCastCache
                        {
                            Sender = enemy,
                            NetworkId = enemy.NetworkId,
                            DangerLevel = menudata.DangerLevel,
                            Tick = (int)Game.Time * 1000
                        });
                    }
                }
            }

            if (Config.InterrupterMenu.Enabled && Config.InterrupterMenu.InterruptibleSpellsFound != 0)
            {
                var menudata = Config.InterrupterMenuValues.FirstOrDefault(info => info.Champion == enemy.Hero);

                if (menudata == null)
                    return;

                if (menudata.Enabled && args.Slot == menudata.SpellSlot)
                {
                    CachedInterruptibleSpells.Add(new ProcessSpellCastCache
                    {
                        Sender = enemy,
                        NetworkId = enemy.NetworkId,
                        DangerLevel = menudata.DangerLevel,
                        Tick = (int)Game.Time * 1000
                    });
                }
            }
        }

        private static void GameObject_OnCreate(GameObject sender, EventArgs args)
        {
            var client = sender as MissileClient;
            if (client != null && client.SData.Name == "AhriOrbMissile")
            {
                QOrbMissile = client;
            }
            if (client != null && client.SData.Name == "AhriOrbReturn")
            {
                QReturnMissile = client;
            }

            if (sender.Name != "Rengar_LeapSound.troy")
                return;

            var gapcloserMenuInfo = Config.AntiGapcloserMenuValues.FirstOrDefault(x => x.Champion == "Rengar");

            if (gapcloserMenuInfo == null || !gapcloserMenuInfo.Enabled)
                return;

            foreach (var rengar in EntityManager.Heroes.Enemies.Where(x => x.IsValidTarget(1000) && x.ChampionName == "Rengar").Where(rengar => rengar.Distance(Player.Instance.Position) < 1000))
            {
                CachedAntiGapclosers.Add(new ProcessSpellCastCache
                {
                    Sender = rengar,
                    NetworkId = rengar.NetworkId,
                    DangerLevel = gapcloserMenuInfo.DangerLevel,
                    Tick = (int)Game.Time * 1000
                });
            }
        }



        private static void GameObject_OnDelete(GameObject sender, EventArgs args)
        {
            var client = sender as MissileClient;
            if (client != null && client.SData.Name == "AhriOrbMissile")
            {
                QOrbMissile = null;
            }
            if (client != null && client.SData.Name == "AhriOrbReturn")
            {
                QReturnMissile = null;
            }
        }

        private static void OnDraw(EventArgs args)
        {
            if(!Config.Drawings.Enabled)
                return;
            
            if (Config.Drawings.DrawW && SpellManager.W.IsLearned)
                Circle.Draw(SpellManager.W.IsReady() ? SharpDX.Color.GreenYellow : SharpDX.Color.Red, SpellManager.W.Range, Config.Drawings.DrawingBorderWidth, Player.Instance.Position);

            if (Config.Drawings.DrawE && SpellManager.E.IsLearned)
                Circle.Draw(SpellManager.E.IsReady() ? SharpDX.Color.DeepPink : SharpDX.Color.Red, SpellManager.E.Range, Config.Drawings.DrawingBorderWidth, Player.Instance.Position);

            if (!Config.Drawings.DrawQPosition)
                return;
            
            var end = new Vector3();
            var position = new Vector3();
            var start = new Vector3();

            if (QOrbMissile != null)
            {
                start = Player.Instance.ServerPosition;
                position = QOrbMissile.Position;
                end = QOrbMissile.EndPosition;
            }
            else if(QReturnMissile != null)
            {
                start = QReturnMissile.StartPosition;
                position = QReturnMissile.Position;
                end = Player.Instance.ServerPosition;
            }

            if (end == Vector3.Zero)
                return;
            
            var polygon1 = new Geometry.Polygon.Rectangle(start.To2D(), end.To2D(), 150);
            polygon1.Draw(Color.White);
            var polygon2 = new Geometry.Polygon.Rectangle(start.To2D(), end.To2D(), 100);
            polygon2.Draw(Color.GreenYellow);
            
            var direction = (end - start).Normalized().To2D();
            var x = Drawing.WorldToScreen((position.To2D() + 90 * direction.Perpendicular()).To3D());
            var y = Drawing.WorldToScreen((position.To2D() - 100 * direction.Perpendicular()).To3D());

            Drawing.DrawLine(x.X, x.Y, y.X, y.Y, 3, Color.DeepPink);
        }
    }
}