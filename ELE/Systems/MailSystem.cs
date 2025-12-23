using System;
using System.Collections.Generic;
using System.Linq;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace ELE.Core.Systems
{
    public class MailSystem
    {
        private readonly ModEntry Mod;

        public const string MailRobin = "JavCombita.ELE_RobinShelterMail";
        public const string MailClint = "JavCombita.ELE_ClintAnalyzerMail";
        public const string MailPierre = "JavCombita.ELE_PierreSpreaderMail";
        public const string MailQi = "JavCombita.ELE_QiUpgradeMail";
        public const string MailDem = "JavCombita.ELE_DemetriusBoosterMail";
        public const string MailEve = "JavCombita.ELE_EvelynBoosterMail";
        public const string MailJodi = "JavCombita.ELE_JodiBoosterMail";
		public const string MailWizard = "JavCombita.ELE_WizardInjectorMail";
        public const string MailKrobus = "JavCombita.ELE_KrobusChaosMail";

        public MailSystem(ModEntry mod)
        {
            this.Mod = mod;
            mod.Helper.Events.GameLoop.DayStarted += OnDayStarted;
        }

        private void OnDayStarted(object sender, DayStartedEventArgs e)
        {
            if (!Context.IsMainPlayer) return;

            // 1. Robin: Día 2+
            if (Game1.stats.DaysPlayed >= 2) 
                TryAddMail(MailRobin, "DaysPlayed >= 2");

            // 2. Clint: Día 5+
            if (Game1.stats.DaysPlayed >= 5) 
                TryAddMail(MailClint, "DaysPlayed >= 5");

            // 3. Pierre: Farming 8 + Amistad 8 corazones
            if (Game1.player.farmingLevel.Value >= 8 && GetFriendshipPoints("Pierre") >= 2000)
                TryAddMail(MailPierre, "Farming 8 + Pierre Friendship 2000");

            // 4. Qi: Tener carta de Pierre
            if (HasReceivedMail(MailPierre))
                TryAddMail(MailQi, "Has Pierre Mail");

            // 5. Boosters (Demetrius, Evelyn, Jodi): Tener carta de Clint
            if (HasReceivedMail(MailClint))
            {
                TryAddMail(MailDem, "Has Clint Mail (Response: Demetrius)");
                TryAddMail(MailEve, "Has Clint Mail (Response: Evelyn)");
                TryAddMail(MailJodi, "Has Clint Mail (Response: Jodi)");
            }
			
			if (Game1.player.farmingLevel.Value >= 8 && GetFriendshipPoints("Wizard") >= 500)
            {
                TryAddMail(MailWizard, "Farming 8 + Wizard 2 Hearts");
            }
			
			if (Game1.player.combatLevel.Value >= 6 && HasReceivedMail(MailWizard))
            {
                TryAddMail(MailKrobus, "Combat 6 + Has Wizard Mail");
            }
        }

        private bool HasReceivedMail(string mailId)
        {
            return Game1.player.mailReceived.Contains(mailId) || Game1.player.mailbox.Contains(mailId);
        }

        private int GetFriendshipPoints(string npcName)
        {
            if (Game1.player.friendshipData.TryGetValue(npcName, out Friendship friendship))
            {
                return friendship.Points;
            }
            return 0;
        }

        private void TryAddMail(string mailId, string reason)
        {
            if (Game1.player.mailReceived.Contains(mailId) || Game1.player.mailbox.Contains(mailId))
            {
                return; 
            }

            Game1.player.mailbox.Add(mailId);
            Mod.Monitor.Log(Mod.Helper.Translation.Get("log.mail_delivery", new { id = mailId, reason = reason }), LogLevel.Info);
        }

        public void ForceAllMails()
        {
            string[] allMails = { MailRobin, MailClint, MailPierre, MailQi, MailDem, MailEve, MailJodi, MailWizard, MailKrobus };
            
            int addedCount = 0;
            foreach (var mail in allMails)
            {
                if (!Game1.player.mailbox.Contains(mail) && !Game1.player.mailReceived.Contains(mail))
                {
                    Game1.player.mailbox.Add(mail);
                    Mod.Monitor.Log(Mod.Helper.Translation.Get("log.mail_forced", new { id = mail }), LogLevel.Alert);
                    addedCount++;
                }
                else
                {
                    if (!Game1.player.mailbox.Contains(mail))
                    {
                        Game1.player.mailbox.Add(mail);
                        Mod.Monitor.Log(Mod.Helper.Translation.Get("log.mail_redelivery", new { id = mail }), LogLevel.Warn);
                        addedCount++;
                    }
                }
            }
            
            if (addedCount == 0)
                Mod.Monitor.Log(Mod.Helper.Translation.Get("log.mail_all_present"), LogLevel.Info);
        }
    }
}