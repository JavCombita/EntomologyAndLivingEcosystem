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

        // IDs de Correos (Deben coincidir EXACTAMENTE con content.json -> Data/mail)
        public const string MailRobin = "JavCombita.ELE_RobinShelterMail";
        public const string MailClint = "JavCombita.ELE_ClintAnalyzerMail";
        public const string MailPierre = "JavCombita.ELE_PierreSpreaderMail";
        public const string MailQi = "JavCombita.ELE_QiUpgradeMail";
        public const string MailDem = "JavCombita.ELE_DemetriusBoosterMail";
        public const string MailEve = "JavCombita.ELE_EvelynBoosterMail";
        public const string MailJodi = "JavCombita.ELE_JodiBoosterMail";

        public MailSystem(ModEntry mod)
        {
            this.Mod = mod;
            mod.Helper.Events.GameLoop.DayStarted += OnDayStarted;
        }

        private void OnDayStarted(object sender, DayStartedEventArgs e)
        {
            if (!Context.IsMainPlayer) return;

            // 1. Robin: Día 2+
            // Lógica: Aparece temprano para enseñar el Shelter.
            if (Game1.stats.DaysPlayed >= 2) 
                TryAddMail(MailRobin, "DaysPlayed >= 2");

            // 2. Clint: Día 5+
            // Lógica: Narrativa de contaminación Joja.
            if (Game1.stats.DaysPlayed >= 5) 
                TryAddMail(MailClint, "DaysPlayed >= 5");

            // 3. Pierre: Farming 8 + Amistad 8 corazones (2000 puntos)
            // Lógica: Recompensa avanzada.
            if (Game1.player.farmingLevel.Value >= 8 && GetFriendshipPoints("Pierre") >= 2000)
                TryAddMail(MailPierre, "Farming 8 + Pierre Friendship 2000");

            // 4. Qi: Tener carta de Pierre (Progression)
            // Lógica: Si ya recibiste la de Pierre, Qi te contacta al día siguiente (o el mismo día si forzamos).
            if (HasReceivedMail(MailPierre))
                TryAddMail(MailQi, "Has Pierre Mail");

            // 5. Boosters (Demetrius, Evelyn, Jodi): Tener carta de Clint
            // Lógica: Respuesta de la comunidad a la contaminación.
            if (HasReceivedMail(MailClint))
            {
                TryAddMail(MailDem, "Has Clint Mail (Response: Demetrius)");
                TryAddMail(MailEve, "Has Clint Mail (Response: Evelyn)");
                TryAddMail(MailJodi, "Has Clint Mail (Response: Jodi)");
            }
        }

        /// <summary>
        /// Verifica si el jugador ya recibió o tiene la carta pendiente.
        /// </summary>
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
            // Verificación crítica: ¿Ya la tiene leída o en el buzón?
            if (Game1.player.mailReceived.Contains(mailId) || Game1.player.mailbox.Contains(mailId))
            {
                return; // Ya la tiene, no hacer nada.
            }

            // Si no la tiene, entregar.
            Game1.player.mailbox.Add(mailId);
            Mod.Monitor.Log($"[ELE Mail] Delivering: {mailId}. Reason: {reason}", LogLevel.Info);
        }

        /// <summary>
        /// Método de Debug para el comando de consola.
        /// </summary>
        public void ForceAllMails()
        {
            string[] allMails = { MailRobin, MailClint, MailPierre, MailQi, MailDem, MailEve, MailJodi };
            
            int addedCount = 0;
            foreach (var mail in allMails)
            {
                if (!Game1.player.mailbox.Contains(mail) && !Game1.player.mailReceived.Contains(mail))
                {
                    Game1.player.mailbox.Add(mail);
                    Mod.Monitor.Log($"[Debug] Forced delivery: {mail}", LogLevel.Alert);
                    addedCount++;
                }
                else
                {
                    // Si ya la leyó pero queremos verla de nuevo para testear, la forzamos en el buzón
                    // (Solo para el comando de debug, no para la lógica normal)
                    if (!Game1.player.mailbox.Contains(mail))
                    {
                        Game1.player.mailbox.Add(mail);
                        Mod.Monitor.Log($"[Debug] Re-delivery (Already known): {mail}", LogLevel.Warn);
                        addedCount++;
                    }
                }
            }
            
            if (addedCount == 0)
                Mod.Monitor.Log("[Debug] All mails are already in the mailbox.", LogLevel.Info);
        }
    }
}