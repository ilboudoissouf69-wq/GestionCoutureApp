using GestionCoutureApp.Data;
using GestionCoutureApp.Helpers;
using GestionCoutureApp.Models;
using Microsoft.EntityFrameworkCore;

namespace GestionCoutureApp.Tests
{
    /// <summary>
    /// Générateur de données de stress partagé entre les tests de charge.
    /// Paramétrable : volume de clients, commandes, pièces, paiements, retours, audit.
    ///
    /// Distribution réaliste ET cas extrêmes :
    /// - ~49 % de pièces en retard (DateFin &lt; aujourd'hui, statut A faire/En cours)
    /// - ~33 % de pièces terminées (section Retrait)
    /// - 5 % de pièces avec RendezVousException
    /// - clients homonymes (recherche texte)
    /// - retours avec DateRdvReprise
    /// - suivis d'alerte retard (test NOT EXISTS)
    /// </summary>
    internal static class StressDataHelper
    {
        private const int TAILLE_LOT = 1_000;
        private static readonly Random _rng = new(42);
        private static readonly string[] Statuts = { "A faire", "En cours", "Terminee", "Livree" };
        private static readonly string[] Types = { "Pantalon", "Chemise", "Robe", "Boubou", "Veste" };
        private static readonly string[] Modes = { "Especes", "Mobile Money", "Virement" };

        /// <summary>
        /// Génère un jeu de données massif dans le contexte fourni.
        /// Le contexte doit déjà avoir son schéma (EnsureCreated ou migrations appliquées).
        /// </summary>
        /// <param name="ctx">Contexte cible (vide, schéma créé).</param>
        /// <param name="nbClients">Nombre de clients.</param>
        /// <param name="nbCommandes">Nombre de commandes.</param>
        /// <param name="nbPieces">Nombre total de pièces (réparties 1-2 par commande).</param>
        /// <param name="nbPaiements">Nombre de paiements.</param>
        /// <param name="nbRetours">Nombre de retours.</param>
        /// <param name="nbAudit">Nombre de lignes d'audit.</param>
        public static void Generer(
            ApplicationDbContext ctx,
            int nbClients = 120_000,
            int nbCommandes = 120_000,
            int nbPieces = 180_000,
            int nbPaiements = 90_000,
            int nbRetours = 10_000,
            int nbAudit = 100_000)
        {
            // ── Boss + couturiers ──────────────────────────────────────────
            ctx.Employes.Add(new Employe
            {
                IdEmploye = 1, Nom = "DIALLO", Prenom = "Boss",
                Identifiant = "boss_stress", MotDePasse = "x",
                Role = "Boss", Statut = "Actif"
            });
            int nbCouturiers = 150;
            for (int i = 2; i <= 1 + nbCouturiers; i++)
                ctx.Employes.Add(new Employe
                {
                    IdEmploye = i, Nom = $"Couturier{i}", Prenom = $"Prenom{i}",
                    Identifiant = $"cout{i}", MotDePasse = "x",
                    Role = "Couturier", Statut = "Actif"
                });
            ctx.ChangeTracker.AutoDetectChangesEnabled = false;
            ctx.SaveChanges();
            ctx.ChangeTracker.Clear();

            // ── Clients par lots ───────────────────────────────────────────
            for (int lot = 0; lot < nbClients / TAILLE_LOT; lot++)
            {
                var batch = new List<Client>(TAILLE_LOT);
                for (int i = 0; i < TAILLE_LOT; i++)
                {
                    int id = lot * TAILLE_LOT + i + 1;
                    // Mélange de noms synthétiques et de noms courants accentués
                    // avec préfixes partagés pour exercer les index de recherche.
                    string nom = (id % 100) switch
                    {
                        0 => "Diop", 1 => "Ndiaye", 2 => "Traoré", 3 => "Ouédraogo",
                        4 => "Diallo", 5 => "Sawadogo", _ => $"NomClient{id}"
                    };
                    string prenom = (id % 100) switch
                    {
                        0 => "Mamadou", 1 => "Awa", 2 => "Aïssata", 3 => "Édouard",
                        4 => "Mariam", 5 => "Issa", _ => $"Prenom{id}"
                    };
                    batch.Add(new Client
                    {
                        IdClient = id, Nom = nom, Prenom = prenom,
                        Telephone = $"7{id:D8}",
                        NomRecherche = GestionCoutureApp.Helpers.TexteHelper.NormaliserPourRecherche($"{nom} {prenom}"),
                        PrenomNomRecherche = GestionCoutureApp.Helpers.TexteHelper.NormaliserPourRecherche($"{prenom} {nom}")
                    });
                }
                ctx.Clients.AddRange(batch);
                ctx.SaveChanges();
                ctx.ChangeTracker.Clear();
            }

            // ── Commandes + pièces + paiements + matériaux ──────────────────
            int pieceId = 0;
            int paiementId = 0;
            int paiementsRestants = nbPaiements;
            int materiels = 0;

            for (int lot = 0; lot < nbCommandes / TAILLE_LOT; lot++)
            {
                var commandes = new List<Commande>(TAILLE_LOT);
                var pieces = new List<PieceCommande>();
                var paiements = new List<Paiement>();
                var materiaux = new List<MaterielSupplement>();

                for (int i = 0; i < TAILLE_LOT; i++)
                {
                    int idCmd = lot * TAILLE_LOT + i + 1;
                    int idClient = (idCmd % nbClients) + 1;
                    int idCout = (idCmd % nbCouturiers) + 2;
                    var statut = Statuts[idCmd % Statuts.Length];
                    var dateDebut = DateTime.Today.AddDays(-(_rng.Next(1, 730)));
                    // ~70 % des pièces actives ont DateFin dans le passé → retard
                    bool enRetard = statut is "A faire" or "En cours" && _rng.Next(100) < 70;
                    var dateFin = enRetard
                        ? DateTime.Today.AddDays(-_rng.Next(1, 60))
                        : dateDebut.AddDays(_rng.Next(3, 21));

                    var commande = new Commande
                    {
                        IdCommande = idCmd, IdClient = idClient,
                        DateDebut = dateDebut, HeureDebut = TimeSpan.FromHours(8),
                        DateFin = dateFin, Statut = statut,
                        TypeVetement = Types[idCmd % Types.Length], MontantTotal = 0m,
                        EstSupprimee = false, EstAnnulee = false,
                        IdOperateurCreation = 1, NomOperateurCreation = "Boss",
                        DateCreation = dateDebut.ToUniversalTime()
                    };
                    commandes.Add(commande);

                    int nbPiecesCmd = (nbPieces > nbCommandes && idCmd % 2 == 0) ? 2 : 1;
                    decimal montantCouture = 0m;
                    for (int p = 0; p < nbPiecesCmd && pieceId < nbPieces; p++)
                    {
                        pieceId++;
                        var pieceStatut = (p == 1 && statut == "A faire") ? "A faire" : statut;
                        decimal montantPiece = 2500m + (pieceId % 10) * 250m;
                        montantCouture += montantPiece;
                        // 5 % des pièces avec RendezVousException
                        DateTime? rdvExc = (pieceId % 20 == 0)
                            ? dateFin.AddDays(_rng.Next(1, 5)).AddHours(_rng.Next(8, 18)) : null;
                        pieces.Add(new PieceCommande
                        {
                            IdPieceCommande = pieceId, IdCommande = idCmd,
                            TypeVetement = Types[pieceId % Types.Length],
                            MontantCouture = montantPiece, Statut = pieceStatut,
                            IdCouturier = idCout,
                            DateTerminee = (pieceStatut == "Terminee" || pieceStatut == "Livree")
                                ? dateDebut.AddDays(2) : null,
                            RendezVousException = rdvExc,
                            DateRdvEffective = DateRdvHelper.CalculerDateRdvEffective(rdvExc, dateFin, null),
                            DateMiParcours = DateRdvHelper.CalculerDateMiParcours(dateDebut.Date.AddHours(8),
                                DateRdvHelper.CalculerDateRdvEffective(rdvExc, dateFin, null)),
                            CommandeActive = true
                        });
                    }
                    commande.MontantTotal = montantCouture;

                    if (idCmd % 10 == 0)
                    {
                        int qte = 1 + idCmd % 3;
                        decimal pu = 750m + idCmd % 5 * 250m;
                        materiels++;
                        materiaux.Add(new MaterielSupplement
                        {
                            IdMateriel = materiels, IdCommande = idCmd,
                            Designation = "Tissu de confection",
                            Quantite = qte, PrixUnitaire = pu,
                            IdOperateur = 1, NomOperateur = "Boss DIALLO"
                        });
                        commande.MontantTotal += qte * pu;
                    }

                    if (paiementsRestants > 0 && idCmd % 4 != 0)
                    {
                        paiementId++; paiementsRestants--;
                        decimal montant = commande.MontantTotal * (_rng.Next(3, 11) / 10m);
                        paiements.Add(new Paiement
                        {
                            IdPaiement = paiementId, IdCommande = idCmd,
                            MontantPaye = montant, DatePaiement = dateDebut.AddDays(1),
                            ModePaiement = Modes[idCmd % Modes.Length],
                            EstAnnule = paiementId % 10 == 0,
                            IdOperateur = 1, NomOperateur = "Boss"
                        });
                    }
                }

                ctx.Commandes.AddRange(commandes); ctx.SaveChanges(); ctx.ChangeTracker.Clear();
                ctx.PiecesCommande.AddRange(pieces); ctx.SaveChanges(); ctx.ChangeTracker.Clear();
                if (materiaux.Count > 0) { ctx.MaterielsSupplements.AddRange(materiaux); ctx.SaveChanges(); ctx.ChangeTracker.Clear(); }
                if (paiements.Count > 0) { ctx.Paiements.AddRange(paiements); ctx.SaveChanges(); ctx.ChangeTracker.Clear(); }
            }

            // ── Retours ───────────────────────────────────────────────────
            if (nbRetours > 0)
            {
                int annee = DateTime.Now.Year; int num = 0;
                for (int lot = 0; lot < nbRetours / TAILLE_LOT; lot++)
                {
                    var batch = new List<Retour>(TAILLE_LOT);
                    for (int i = 0; i < TAILLE_LOT; i++)
                    {
                        num++; int idRetour = lot * TAILLE_LOT + i + 1;
                        // Reconstituer la relation réelle pièce/commande. Les pièces
                        // supplémentaires ne concernent que les commandes paires.
                        int idCmd = _rng.Next(1, nbCommandes + 1);
                        int premieresPiecesAvant = idCmd - 1
                            + (nbPieces > nbCommandes ? (idCmd - 1) / 2 : 0);
                        int idPiece = premieresPiecesAvant + 1;
                        bool commandeDeuxPieces = nbPieces > nbCommandes && idCmd % 2 == 0;
                        if (commandeDeuxPieces && idPiece < nbPieces && _rng.Next(2) == 1)
                            idPiece++;
                        var dateSig = DateTime.Now.AddDays(-_rng.Next(1, 90));
                        string statut = (idRetour % 5) switch
                        {
                            0 => "Signale", 1 => "En reprise", 2 => "Termine", 3 => "Rendu", _ => "Pret"
                        };
                        batch.Add(new Retour
                        {
                            IdRetour = idRetour, IdCommande = idCmd, IdPieceCommande = idPiece,
                            NumeroRetour = $"RET-{annee}-{num:D5}", Statut = statut,
                            DateSignalement = dateSig, DescriptionProbleme = "Défaut de couture",
                            EstAnnule = false, IdCouturier = (idCmd % 150) + 2,
                            IdOperateurEnregistrement = 1, NomOperateurEnregistrement = "Boss",
                            DateRdvReprise = idRetour % 3 == 0
                                ? DateTime.Now.AddDays(_rng.Next(-5, 10)) : null,
                            DateResolution = statut is "Termine" or "Rendu"
                                ? dateSig.AddDays(_rng.Next(1, 14)) : null
                        });
                    }
                    ctx.Retours.AddRange(batch); ctx.SaveChanges(); ctx.ChangeTracker.Clear();
                }
            }

            // ── Suivis alerte retard (NOT EXISTS) ──────────────────────────
            int nbSuivis = nbPieces / 20;
            if (nbSuivis > 0)
            {
                for (int lot = 0; lot < nbSuivis / TAILLE_LOT; lot++)
                {
                    var batch = new List<SuiviAlerteRetard>(TAILLE_LOT);
                    for (int i = 0; i < TAILLE_LOT; i++)
                    {
                        int n = lot * TAILLE_LOT + i + 1;
                        string type = n % 3 == 0 ? "Piece" : "Commande";
                        int idCible = n % 3 == 0 ? (n * 3) : n;
                        batch.Add(new SuiviAlerteRetard
                        {
                            TypeCible = type, IdCible = idCible, IdClient = (n % 1000) + 1,
                            DateRdvTraite = DateTime.Now.AddDays(-_rng.Next(1, 30)),
                            NbPreventions = 1 + (n % 3), NbReports = n % 2,
                            DateDernierMessage = DateTime.Now.AddDays(-_rng.Next(1, 15))
                        });
                    }
                    ctx.SuivisAlerteRetard.AddRange(batch); ctx.SaveChanges(); ctx.ChangeTracker.Clear();
                }
            }

            // ── Journal d'audit ──────────────────────────────────────────
            if (nbAudit > 0)
            {
                var actions = new[] { "CREATION", "MODIFICATION", "SUPPRESSION", "PAIEMENT", "LIVRAISON" };
                string? hashPrev = null;
                for (int lot = 0; lot < nbAudit / 1000; lot++)
                {
                    var batch = new List<JournalAudit>(1000);
                    for (int i = 0; i < 1000; i++)
                    {
                        int id = lot * 1000 + i + 1;
                        string data = $"Action{id}";
                        string hash = Convert.ToHexString(
                            System.Security.Cryptography.SHA256.HashData(
                                System.Text.Encoding.UTF8.GetBytes((hashPrev ?? "") + data)));
                        batch.Add(new JournalAudit
                        {
                            IdJournal = id, DateHeureUtc = DateTime.UtcNow.AddMinutes(-id),
                            TypeAction = actions[id % actions.Length], Entite = "Commande",
                            IdEntite = (id % nbCommandes) + 1, NomOperateur = "Boss",
                            IdOperateur = 1, HashPrecedent = hashPrev, HashCourant = hash
                        });
                        hashPrev = hash;
                    }
                    ctx.JournalAudit.AddRange(batch); ctx.SaveChanges(); ctx.ChangeTracker.Clear();
                }
            }

            // ── Dépenses ─────────────────────────────────────────────────
            int idDepense = 0;
            for (int lot = 0; lot < 10; lot++)
            {
                var depenses = new List<Depense>(1000);
                for (int i = 0; i < 1000; i++)
                {
                    idDepense++;
                    depenses.Add(new Depense
                    {
                        IdDepense = idDepense,
                        Categorie = idDepense % 2 == 0 ? "Matériel & Entretien" : "Charges fixes",
                        TypeDepense = idDepense % 2 == 0 ? "Achat fournitures" : "Électricité",
                        Montant = 5000m + idDepense % 40 * 500m,
                        DateDepense = DateTime.Today.AddDays(-(idDepense % 730)),
                        Description = $"Dépense de test {idDepense}",
                        IdOperateur = 1, NomOperateur = "Boss DIALLO",
                        StatutValidation = idDepense % 20 == 0 ? "En attente" : "Validee",
                        EstAnnulee = idDepense % 50 == 0
                    });
                }
                ctx.Depenses.AddRange(depenses); ctx.SaveChanges(); ctx.ChangeTracker.Clear();
            }

            // ── Commissions (24 périodes) ─────────────────────────────────
            var commissions = new List<Commission>(nbCouturiers * 24);
            for (int mois = 0; mois < 24; mois++)
            {
                var debutPeriode = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(-mois);
                foreach (int idCouturier in Enumerable.Range(2, nbCouturiers))
                {
                    commissions.Add(new Commission
                    {
                        IdEmploye = idCouturier,
                        NomEmployeSnapshot = $"Prenom{idCouturier} Couturier{idCouturier}",
                        DateDebutPeriode = debutPeriode,
                        DateFinPeriode = debutPeriode.AddMonths(1).AddDays(-1),
                        DateCalcul = debutPeriode.AddMonths(1).AddDays(-1),
                        BaseCalcul = "Encaisse", Pourcentage = 10m,
                        BaseMontant = 100000m + (idCouturier % 100) * 1000m,
                        MontantCommission = 10000m + (idCouturier % 100) * 100m,
                        PrimeQualite = idCouturier % 5 == 0 ? 2500m : 0m,
                        NbCommandes = 10 + idCouturier % 50,
                        IdOperateur = 1, NomOperateur = "Boss DIALLO",
                        EstAnnulee = mois == 0 && idCouturier % 20 == 0
                    });
                }
            }
            ctx.Commissions.AddRange(commissions);
            ctx.SaveChanges();
            ctx.ChangeTracker.Clear();

            ctx.ChangeTracker.AutoDetectChangesEnabled = true;
        }
    }
}
