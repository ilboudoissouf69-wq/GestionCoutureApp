using GestionCoutureApp.Helpers;
using GestionCoutureApp.Models;

namespace GestionCoutureApp.Data
{
    /// <summary>
    /// Données de démonstration massives :
    /// — 350 clients
    /// — 10 secrétaires
    /// — 150 couturiers
    /// — ~600 commandes réparties sur 60 jours
    /// — ~800 paiements (partiels et complets)
    /// </summary>
    public static class DemoDataSeeder
    {
        private static readonly Random Rng = new Random(42); // seed fixe = reproductible

        public static void Seeder(ApplicationDbContext context)
        {
            if (context.Clients.Any()) return;

            // ============================================================
            // DONNÉES DE BASE
            // ============================================================
            var prenomsH = new[] { "Mamadou","Boubacar","Idrissa","Souleymane","Abdoulaye",
                "Hamidou","Seydou","Adama","Ibrahim","Oumarou","Moussa","Drissa","Alassane",
                "Issouf","Modibo","Tiécoura","Ousmane","Lamine","Samba","Cheick" };
            var prenomsF = new[] { "Aminata","Mariam","Aïssata","Fatoumata","Rasmata",
                "Haoua","Kadiatou","Nafissatou","Salimata","Roukiatou","Bintou","Assétou",
                "Djeneba","Maimouna","Oumou","Kadidiatou","Ramata","Zénabou","Balkissa","Safiatou" };
            var noms = new[] { "Diallo","Konaté","Traoré","Coulibaly","Zerbo","Compaoré",
                "Ouedraogo","Sawadogo","Kaboré","Nikiéma","Barro","Sorgho","Tapsoba","Yameogo",
                "Zongo","Sankara","Badini","Tiendrebeogo","Rouamba","Lompo","Dicko","Cissé",
                "Dembélé","Sanogo","Fofana","Keita","Sanou","Drabo","Kouyaté","Barry" };
            var villes = new[] { "Ouagadougou","Bobo-Dioulasso","Koudougou","Ouahigouya",
                "Banfora","Dédougou","Kaya","Tenkodogo","Fada N'Gourma","Manga" };

            // Types de vêtements
            var typesNoms = new[] { "Pantalon","Chemise","Robe","Boubou","Veste" };
            var descriptions = new Dictionary<string, string[]>
            {
                ["Pantalon"] = new[] { "Coupe droite classique","Coupe slim / ajustée","Avec poches latérales","Avec pinces" },
                ["Chemise"]  = new[] { "Col chemise classique","Col V","Manches longues","Manches courtes" },
                ["Robe"]     = new[] { "Robe longue","Robe midi","Robe courte","Avec ceinture" },
                ["Boubou"]   = new[] { "Boubou classique","Boubou brodé","Boubou avec poche" },
                ["Veste"]    = new[] { "Veste classique","Veste cintrée","Avec boutons","Sans manches" },
            };
            var prixBase = new Dictionary<string, decimal>
            {
                ["Pantalon"] = 5_000m, ["Chemise"] = 4_000m, ["Robe"] = 8_000m,
                ["Boubou"]   = 6_000m, ["Veste"]   = 10_000m
            };
            var statuts = new[] { "A faire","En cours","Terminee","Livree" };
            var modes   = new[] { "Especes","Mobile Money","Virement","Cheque" };

            // ============================================================
            // 1. EMPLOYÉS — Boss déjà créé, on ajoute secrétaires + couturiers
            // ============================================================
            var secretaires = new List<Employe>();
            for (int i = 1; i <= 10; i++)
            {
                string prenom = prenomsF[(i - 1) % prenomsF.Length];
                string nom    = noms[(i + 5) % noms.Length];
                secretaires.Add(new Employe
                {
                    Nom = nom, Prenom = prenom,
                    Identifiant = $"secretaire{i:D2}",
                    MotDePasse  = PasswordHasher.Hasher($"sec{i:D2}pass"),
                    Role = "Secretaire", Statut = "Actif"
                });
            }
            context.Employes.AddRange(secretaires);
            context.SaveChanges();

            var couturiers = new List<Employe>();
            for (int i = 1; i <= 150; i++)
            {
                bool homme = i % 2 == 0;
                string prenom = homme
                    ? prenomsH[(i - 1) % prenomsH.Length]
                    : prenomsF[(i - 1) % prenomsF.Length];
                string nom = noms[i % noms.Length];
                couturiers.Add(new Employe
                {
                    Nom = nom, Prenom = prenom,
                    Identifiant = $"couturier{i:D3}",
                    MotDePasse  = PasswordHasher.Hasher($"cou{i:D3}pass"),
                    Role = "Couturier", Statut = i <= 140 ? "Actif" : "Suspendu"
                });
            }
            context.Employes.AddRange(couturiers);
            context.SaveChanges();

            var couturiersActifs = couturiers.Where(c => c.Statut == "Actif").ToList();
            var operateurs       = secretaires.Concat(
                context.Employes.Where(e => e.Role == "Boss").ToList()).ToList();

            // ============================================================
            // 2. CLIENTS — 350
            // ============================================================
            var clients = new List<Client>();
            for (int i = 1; i <= 350; i++)
            {
                bool femme  = i % 3 != 0;
                string prenom = femme
                    ? prenomsF[(i - 1) % prenomsF.Length]
                    : prenomsH[(i - 1) % prenomsH.Length];
                string nom = noms[i % noms.Length];
                clients.Add(new Client
                {
                    Nom      = nom,
                    Prenom   = prenom + (i > noms.Length ? $" {i}" : ""),
                    Telephone = $"7{Rng.Next(0,9)} {Rng.Next(10,99)} {Rng.Next(10,99)} {Rng.Next(10,99)}"
                });
            }
            context.Clients.AddRange(clients);
            context.SaveChanges();

            // ============================================================
            // 3. COMMANDES — ~620 réparties sur 60 jours
            // ============================================================
            var commandes = new List<Commande>();
            var today = DateTime.Today;

            for (int i = 0; i < 620; i++)
            {
                var client    = clients[i % clients.Count];
                var couturier = couturiersActifs[i % couturiersActifs.Count];
                var typeNom   = typesNoms[i % typesNoms.Length];
                var desc      = descriptions[typeNom][i % descriptions[typeNom].Length];
                int debutJours = Rng.Next(1, 60);
                var dateDebut  = today.AddDays(-debutJours);
                var dateFin    = dateDebut.AddDays(Rng.Next(3, 14));
                decimal montant = prixBase[typeNom] + (Rng.Next(0, 8) * 500m);

                // Statut cohérent avec les dates
                string statut;
                if (dateFin < today.AddDays(-5))
                    statut = i % 3 == 0 ? "Livree" : "Terminee";
                else if (dateFin < today)
                    statut = i % 4 == 0 ? "Livree" : "En cours"; // certains sont en retard
                else
                    statut = i % 5 == 0 ? "A faire" : "En cours";

                commandes.Add(new Commande
                {
                    IdClient      = client.IdClient,
                    IdCouturier   = couturier.IdEmploye,
                    TypeVetement  = typeNom,
                    DescriptionPrecision = desc,
                    DateDebut     = dateDebut,
                    DateFin       = dateFin,
                    Statut        = statut,
                    MontantTotal  = montant,
                    Mesures       = MesuresPour(typeNom)
                });
            }
            context.Commandes.AddRange(commandes);
            context.SaveChanges();

            // ============================================================
            // 4. PAIEMENTS — ~800
            // ============================================================
            var paiements = new List<Paiement>();
            int recuSeq   = 1;

            foreach (var cmd in commandes)
            {
                var operateur = operateurs[recuSeq % operateurs.Count];
                decimal montant = cmd.MontantTotal;

                if (cmd.Statut == "Livree" || cmd.Statut == "Terminee")
                {
                    // 70% soldées, 30% paiement partiel
                    if (Rng.Next(10) < 7)
                    {
                        // Soldée en 1 ou 2 versements
                        if (Rng.Next(2) == 0)
                        {
                            paiements.Add(Paiement(cmd, montant, modes[recuSeq % modes.Length],
                                operateur, montant, montant, cmd.DateDebut.AddDays(1), recuSeq++));
                        }
                        else
                        {
                            decimal v1 = Math.Round(montant * 0.6m, 0);
                            decimal v2 = montant - v1;
                            paiements.Add(Paiement(cmd, v1, "Especes",
                                operateur, montant, montant, cmd.DateDebut, recuSeq++));
                            paiements.Add(Paiement(cmd, v2, modes[recuSeq % modes.Length],
                                operateur, montant, montant - v1, cmd.DateFin.AddDays(-1), recuSeq++));
                        }
                    }
                    else
                    {
                        // Partiel — acompte seulement
                        decimal acompte = Math.Round(montant * 0.5m, 0);
                        paiements.Add(Paiement(cmd, acompte, "Especes",
                            operateur, montant, montant, cmd.DateDebut, recuSeq++));
                    }
                }
                else if (cmd.Statut == "En cours")
                {
                    // 60% ont déjà versé un acompte
                    if (Rng.Next(10) < 6)
                    {
                        decimal acompte = Math.Round(montant * (0.3m + (decimal)Rng.Next(0, 4) * 0.1m), 0);
                        paiements.Add(Paiement(cmd, acompte, modes[recuSeq % modes.Length],
                            operateur, montant, montant, cmd.DateDebut, recuSeq++));
                    }
                }
                // "A faire" → pas encore de paiement
            }

            context.Paiements.AddRange(paiements);
            context.SaveChanges();
        }

        // ============================================================
        // Helper : crée un Paiement
        // ============================================================
        private static Paiement Paiement(Commande cmd, decimal montantPaye, string mode,
            Employe operateur, decimal totalCommande, decimal resteAvant,
            DateTime date, int seq)
        {
            return new Paiement
            {
                IdCommande           = cmd.IdCommande,
                MontantPaye          = montantPaye,
                ModePaiement         = mode,
                RecuNumero           = $"REC-{date:yyyyMMdd}-{seq:D4}",
                IdOperateur          = operateur.IdEmploye,
                NomOperateur         = operateur.Prenom + " " + operateur.Nom,
                DatePaiement         = date,
                MontantTotalCommande = totalCommande,
                ResteAvantPaiement   = resteAvant,
                EstAnnule            = false
            };
        }

        // ============================================================
        // Mesures réalistes par type
        // ============================================================
        private static List<Mesure> MesuresPour(string type)
        {
            var r = new Random();
            return type switch
            {
                "Pantalon" => new List<Mesure>
                {
                    new() { NomMesure = "Longueur",       Valeur = $"{r.Next(95,110)}" },
                    new() { NomMesure = "Tour de taille", Valeur = $"{r.Next(76,102)}" },
                    new() { NomMesure = "Tour de cuisse", Valeur = $"{r.Next(52,68)}"  },
                    new() { NomMesure = "Entrejambe",     Valeur = $"{r.Next(70,82)}"  },
                    new() { NomMesure = "Bas de patte",   Valeur = $"{r.Next(16,22)}"  },
                },
                "Chemise" => new List<Mesure>
                {
                    new() { NomMesure = "Longueur dos",     Valeur = $"{r.Next(68,78)}" },
                    new() { NomMesure = "Tour de poitrine", Valeur = $"{r.Next(88,108)}" },
                    new() { NomMesure = "Tour d'épaule",    Valeur = $"{r.Next(40,48)}"  },
                    new() { NomMesure = "Longueur manche",  Valeur = $"{r.Next(58,68)}"  },
                    new() { NomMesure = "Tour de poignet",  Valeur = $"{r.Next(18,24)}"  },
                },
                "Robe" => new List<Mesure>
                {
                    new() { NomMesure = "Longueur",         Valeur = $"{r.Next(100,135)}" },
                    new() { NomMesure = "Tour de poitrine", Valeur = $"{r.Next(84,102)}"  },
                    new() { NomMesure = "Tour de taille",   Valeur = $"{r.Next(64,86)}"   },
                    new() { NomMesure = "Tour de hanches",  Valeur = $"{r.Next(90,110)}"  },
                    new() { NomMesure = "Longueur épaule",  Valeur = $"{r.Next(34,42)}"   },
                },
                "Boubou" => new List<Mesure>
                {
                    new() { NomMesure = "Longueur",         Valeur = $"{r.Next(120,145)}" },
                    new() { NomMesure = "Tour de poitrine", Valeur = $"{r.Next(100,120)}" },
                    new() { NomMesure = "Longueur manche",  Valeur = $"{r.Next(64,76)}"   },
                    new() { NomMesure = "Largeur col",      Valeur = $"{r.Next(14,20)}"   },
                },
                "Veste" => new List<Mesure>
                {
                    new() { NomMesure = "Longueur",          Valeur = $"{r.Next(62,74)}"  },
                    new() { NomMesure = "Tour de poitrine",  Valeur = $"{r.Next(92,108)}" },
                    new() { NomMesure = "Tour de taille",    Valeur = $"{r.Next(82,96)}"  },
                    new() { NomMesure = "Longueur manche",   Valeur = $"{r.Next(58,66)}"  },
                    new() { NomMesure = "Épaisseur épaule",  Valeur = $"{r.Next(42,50)}"  },
                },
                _ => new List<Mesure>()
            };
        }
    }
}
