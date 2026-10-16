using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GestionCoutureApp.Models;

namespace GestionCoutureApp.Views
{
    /// <summary>
    /// Étape 5 — Extension de CommandesView pour le brouillon automatique et la duplication de pièces.
    /// Ce fichier contient les méthodes partielles à intégrer dans CommandesView.cs.
    /// </summary>
    public partial class CommandesView
    {
        // ================================================================
        // A) BROUILLON AUTOMATIQUE
        // ================================================================

        /// <summary>
        /// Initialise le timer de sauvegarde automatique du brouillon (debounce 3 secondes).
        /// À appeler dans le constructeur APRÈS l'initialisation des services.
        /// </summary>
        private void InitialiserBrouillonAutomatique()
        {
            // Timer debounce 3 secondes
            _timerBrouillon = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            _timerBrouillon.Tick += async (s, e) =>
            {
                _timerBrouillon.Stop();
                await SauvegarderBrouillonAsync();
            };

            // Hook sur Unloaded : sauvegarder avant de quitter la page
            Unloaded += async (s, e) =>
            {
                if (_modalModeCreation && OverlayModalCommande.Visibility == Visibility.Visible)
                {
                    await SauvegarderBrouillonAsync();
                }
            };
        }

        /// <summary>
        /// Vérifie au chargement de la page si un brouillon existe et affiche le bandeau.
        /// À appeler dans Loaded après ChargerCommandes().
        /// </summary>
        private async Task VerifierBrouillonExistantAsync()
        {
            var (idEmp, _) = OperateurConnecte();
            if (idEmp == 0) return;

            var infos = await _brouillonService.ObtenirInfosBrouillonAsync(idEmp);
            if (infos == null)
            {
                if (BandeauBrouillon != null)
                    BandeauBrouillon.Visibility = Visibility.Collapsed;
                return;
            }

            // Afficher le bandeau avec les informations du brouillon
            if (BandeauBrouillon != null && TxtInfoBrouillon != null)
            {
                string dateStr = infos.Value.date.ToString("dd/MM/yyyy HH:mm");
                TxtInfoBrouillon.Text = $"Vous avez une commande en cours ({infos.Value.nomClient}, sauvegardé le {dateStr})";
                BandeauBrouillon.Visibility = Visibility.Visible;
            }
        }

        /// <summary>
        /// Déclenche le timer de sauvegarde (debounce).
        /// À appeler après chaque modification dans le formulaire modal.
        /// </summary>
        private void DeclenencherSauvegardeBrouillon()
        {
            // Seulement en mode création, pas en édition
            if (!_modalModeCreation || _commandeEnEdition != null)
                return;

            if (_brouillonEnCours)
                return;

            _timerBrouillon?.Stop();
            _timerBrouillon?.Start();
        }

        /// <summary>
        /// Sauvegarde le brouillon en base de données (sans bloquer l'UI).
        /// </summary>
        private async Task SauvegarderBrouillonAsync()
        {
            if (_brouillonEnCours) return;
            _brouillonEnCours = true;

            try
            {
                var (idEmp, _) = OperateurConnecte();
                if (idEmp == 0) return;

                // Ne rien sauvegarder si la modale n'est pas ouverte ou si on est en mode édition
                if (OverlayModalCommande.Visibility != Visibility.Visible || !_modalModeCreation)
                    return;

                var brouillon = new BrouillonCommandeDto
                {
                    IdClient = CmbModalClient.SelectedValue as int?,
                    NomClient = (CmbModalClient.SelectedItem as Client)?.Nom ?? "Client non défini",
                    Acompte = decimal.TryParse(TxtModalAcompte.Text.Trim().Replace(" ", ""), out decimal acompte) ? acompte : 0,
                    DateFin = DpModalDateFin.SelectedDate,
                    HeureFin = TimeSpan.TryParse(TxtModalHeure.Text.Trim(), out TimeSpan heure) ? heure : default(TimeSpan?),
                    Notes = string.Empty,  // Si vous avez un champ notes, l'ajouter ici
                    Pieces = new List<BrouillonPieceDto>()
                };

                // Capturer les pièces
                foreach (var carte in _cartesPieces)
                {
                    var piece = new BrouillonPieceDto
                    {
                        IdTypeVetement = carte.CmbType.SelectedValue as int?,
                        TypeVetement = ((dynamic?)carte.CmbType.SelectedItem)?.DisplayText?.ToString(),
                        IdCouturier = carte.CmbCouturier.SelectedValue as int?,
                        Prix = decimal.TryParse(carte.TxtPrix.Text.Trim().Replace(" ", ""), out decimal prix) ? prix : 0,
                        Description = carte.CmbDesc.Text?.Trim() ?? "",
                        CheminPhoto = carte.CheminPhoto,  // Chemin uniquement, pas de base64
                        Mesures = new List<BrouillonMesureDto>(),
                        Materiaux = new List<BrouillonMaterielDto>()
                    };

                    // Capturer les mesures
                    foreach (UIElement child in carte.PanelMesures.Children)
                    {
                        if (child is StackPanel sp && sp.Children.Count >= 2)
                        {
                            if (sp.Children[0] is TextBlock lbl && sp.Children[1] is TextBox txt)
                            {
                                string nomMesure = lbl.Text.Replace(" (cm)", "").Replace(" *", "").Trim();
                                string valeur = txt.Text.Trim();
                                if (!string.IsNullOrEmpty(valeur))
                                {
                                    piece.Mesures.Add(new BrouillonMesureDto
                                    {
                                        Nom = nomMesure,
                                        Valeur = valeur
                                    });
                                }
                            }
                        }
                    }

                    // Capturer les matériaux du buffer
                    foreach (var mat in carte.MateriauxBuffer)
                    {
                        piece.Materiaux.Add(new BrouillonMaterielDto
                        {
                            Nom = mat.Designation,
                            Prix = mat.PrixUnitaire,
                            Quantite = mat.Quantite
                        });
                    }

                    brouillon.Pieces.Add(piece);
                }

                await _brouillonService.SauvegarderBrouillonAsync(idEmp, brouillon);
            }
            catch (Exception ex)
            {
                // Échec silencieux : le brouillon n'est qu'une aide, il ne doit jamais bloquer
                System.Diagnostics.Debug.WriteLine($"[BROUILLON] Erreur de sauvegarde : {ex.Message}");
            }
            finally
            {
                _brouillonEnCours = false;
            }
        }

        /// <summary>
        /// Charge et restaure un brouillon existant dans le formulaire modal.
        /// À appeler depuis le bouton "Reprendre" du bandeau.
        /// </summary>
        private async void BtnReprendreBrouillon_Click(object sender, RoutedEventArgs e)
        {
            var (idEmp, _) = OperateurConnecte();
            if (idEmp == 0) return;

            try
            {
                var brouillon = await _brouillonService.ChargerBrouillonAsync(idEmp);
                if (brouillon == null)
                {
                    MessageBox.Show("Aucun brouillon trouvé.", "Brouillon",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // Ouvrir la modale en mode création
                OuvrirModalCreation();

                // Restaurer les champs en-tête
                if (brouillon.IdClient.HasValue)
                {
                    CmbModalClient.SelectedValue = brouillon.IdClient.Value;
                }
                if (brouillon.DateFin.HasValue)
                {
                    DpModalDateFin.SelectedDate = brouillon.DateFin.Value;
                }
                if (brouillon.HeureFin.HasValue)
                {
                    TxtModalHeure.Text = brouillon.HeureFin.Value.ToString(@"hh\:mm");
                }
                TxtModalAcompte.Text = brouillon.Acompte.ToString("0");

                // Vider les cartes existantes et recréer depuis le brouillon
                _cartesPieces.Clear();
                PanneauCartesPieces.Children.Clear();

                foreach (var piece in brouillon.Pieces)
                {
                    // Créer une carte vide
                    AjouterCarte(null);
                    var carte = _cartesPieces.Last();

                    // Restaurer le type
                    if (piece.IdTypeVetement.HasValue)
                    {
                        carte.CmbType.SelectedValue = piece.IdTypeVetement.Value;
                    }

                    // Restaurer le couturier
                    if (piece.IdCouturier.HasValue)
                    {
                        carte.CmbCouturier.SelectedValue = piece.IdCouturier.Value;
                    }

                    // Restaurer le prix
                    carte.TxtPrix.Text = piece.Prix.ToString("0");

                    // Restaurer la description
                    carte.CmbDesc.Text = piece.Description ?? "";

                    // Restaurer la photo (chemin uniquement)
                    if (!string.IsNullOrEmpty(piece.CheminPhoto) && System.IO.File.Exists(piece.CheminPhoto))
                    {
                        AfficherPhotoSurCarte(carte, piece.CheminPhoto);
                    }

                    // Restaurer les mesures (après sélection du type)
                    // Les mesures seront affichées par CmbTypeCarte_SelectionChanged
                    await Task.Delay(50);  // Petite pause pour que l'UI se rafraîchisse

                    foreach (var mesure in piece.Mesures)
                    {
                        foreach (UIElement child in carte.PanelMesures.Children)
                        {
                            if (child is StackPanel sp && sp.Children.Count >= 2)
                            {
                                if (sp.Children[0] is TextBlock lbl && sp.Children[1] is TextBox txt)
                                {
                                    string nomMesure = lbl.Text.Replace(" (cm)", "").Replace(" *", "").Trim();
                                    if (nomMesure == mesure.Nom)
                                    {
                                        txt.Text = mesure.Valeur ?? "";
                                        break;
                                    }
                                }
                            }
                        }
                    }

                    // Restaurer les matériaux
                    carte.MateriauxBuffer.Clear();
                    foreach (var mat in piece.Materiaux)
                    {
                        var materiel = new MaterielSupplement
                        {
                            Designation = mat.Nom ?? "",
                            PrixUnitaire = mat.Prix,
                            Quantite = mat.Quantite
                        };
                        carte.MateriauxBuffer.Add(materiel);
                    }
                    RafraichirAffichageMateriauxCarte(carte);
                }

                // Masquer le bandeau brouillon
                if (BandeauBrouillon != null)
                    BandeauBrouillon.Visibility = Visibility.Collapsed;

                MessageBox.Show($"Brouillon restauré avec succès : {brouillon.Pieces.Count} pièce(s).",
                    "Brouillon", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors du chargement du brouillon :\n{ex.Message}",
                    "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Abandonne le brouillon en cours.
        /// À appeler depuis le bouton "Abandonner" du bandeau.
        /// </summary>
        private async void BtnAbandonnerBrouillon_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Voulez-vous vraiment abandonner ce brouillon ?\nCette action est irréversible.",
                "Abandonner le brouillon",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return;

            var (idEmp, _) = OperateurConnecte();
            if (idEmp == 0) return;

            try
            {
                await _brouillonService.SupprimerBrouillonAsync(idEmp);
                if (BandeauBrouillon != null)
                    BandeauBrouillon.Visibility = Visibility.Collapsed;

                MessageBox.Show("Brouillon abandonné.", "Brouillon",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors de la suppression du brouillon :\n{ex.Message}",
                    "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Supprime le brouillon après validation de la commande.
        /// À appeler dans BtnValiderCommande_Click APRÈS enregistrement réussi.
        /// </summary>
        private async Task SupprimerBrouillonApresValidationAsync()
        {
            var (idEmp, _) = OperateurConnecte();
            if (idEmp == 0) return;

            try
            {
                await _brouillonService.SupprimerBrouillonAsync(idEmp);
            }
            catch
            {
                // Silencieux : l'important est que la commande soit créée
            }
        }

        // ================================================================
        // B) DUPLICATION DE PIÈCES
        // ================================================================

        /* ÉTAPE 5 : Méthodes BtnDupliquerCartePiece_Click et CreerBoutonIconeSurBandeau
         * transférées vers CommandesView.cs pour éviter la duplication.
         * Voir CommandesView.cs pour l'implémentation complète.
         */

        /// <summary>
        /// Duplique une pièce (carte) N fois.
        /// Les copies sont insérées juste après la pièce d'origine.
        /// </summary>
        private void DupliquerPiece(Cartepiece carteSource, int nbCopies)
        {
            try
            {
                int indexSource = _cartesPieces.IndexOf(carteSource);
                if (indexSource < 0)
                    return;

                // Position d'insertion dans PanneauCartesPieces
                int posInsert = indexSource + 1;

                for (int i = 0; i < nbCopies; i++)
                {
                    // Créer une nouvelle carte vide
                    AjouterCarte(null, posInsert);
                    var nouvelleCarte = _cartesPieces[posInsert];

                    // Copier les données de la source SAUF photo, statut, commission, DateTerminee
                    // Type de vêtement
                    if (carteSource.CmbType.SelectedValue != null)
                    {
                        nouvelleCarte.CmbType.SelectedValue = carteSource.CmbType.SelectedValue;
                    }

                    // Couturier
                    if (carteSource.CmbCouturier.SelectedValue != null)
                    {
                        nouvelleCarte.CmbCouturier.SelectedValue = carteSource.CmbCouturier.SelectedValue;
                    }

                    // Prix
                    nouvelleCarte.TxtPrix.Text = carteSource.TxtPrix.Text;

                    // Description
                    nouvelleCarte.CmbDesc.Text = carteSource.CmbDesc.Text;

                    // Statut : toujours "A faire" pour une nouvelle pièce
                    nouvelleCarte.CmbStatutPiece.SelectedIndex = 0;

                    // Copier les mesures
                    // Attendre que le type soit appliqué (CmbTypeCarte_SelectionChanged)
                    Dispatcher.InvokeAsync(async () =>
                    {
                        await Task.Delay(50);  // Petite pause pour que l'UI se rafraîchisse

                        // Copier les valeurs des mesures
                        var mesuresSource = new List<(string nom, string valeur)>();
                        foreach (UIElement child in carteSource.PanelMesures.Children)
                        {
                            if (child is StackPanel sp && sp.Children.Count >= 2)
                            {
                                if (sp.Children[0] is TextBlock lbl && sp.Children[1] is TextBox txt)
                                {
                                    string nomMesure = lbl.Text.Replace(" (cm)", "").Replace(" *", "").Trim();
                                    string valeur = txt.Text.Trim();
                                    mesuresSource.Add((nomMesure, valeur));
                                }
                            }
                        }

                        // Appliquer aux mesures de la nouvelle carte
                        foreach (var (nom, valeur) in mesuresSource)
                        {
                            foreach (UIElement child in nouvelleCarte.PanelMesures.Children)
                            {
                                if (child is StackPanel sp && sp.Children.Count >= 2)
                                {
                                    if (sp.Children[0] is TextBlock lbl && sp.Children[1] is TextBox txt)
                                    {
                                        string nomMesure = lbl.Text.Replace(" (cm)", "").Replace(" *", "").Trim();
                                        if (nomMesure == nom)
                                        {
                                            txt.Text = valeur;
                                            break;
                                        }
                                    }
                                }
                            }
                        }
                    });

                    // PAS de copie de photo (respecte la spécification)
                    // PAS de copie de commission ni DateTerminee (pièce neuve)

                    posInsert++;
                }

                // Recalculer le total si on est en mode édition
                if (_commandeEnEdition != null)
                {
                    RecalculerTotalCommande();
                }

                // Déclencher sauvegarde brouillon si on est en création
                DeclenencherSauvegardeBrouillon();

                MessageBox.Show($"{nbCopies} copie(s) créée(s) avec succès.",
                    "Duplication", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors de la duplication :\n{ex.Message}",
                    "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Version surchargée de AjouterCarte qui permet d'insérer à une position spécifique.
        /// </summary>
        private void AjouterCarte(PieceCommande? pieceExistante, int? positionInsert = null)
        {
            // Si position spécifiée, insérer à cet index, sinon ajouter à la fin
            if (positionInsert.HasValue && positionInsert.Value >= 0 && positionInsert.Value <= _cartesPieces.Count)
            {
                // Créer la carte normalement (elle sera ajoutée à la fin)
                AjouterCarte(pieceExistante);
                
                // Récupérer la carte qui vient d'être ajoutée
                var carte = _cartesPieces[_cartesPieces.Count - 1];
                var conteneur = carte.Conteneur;

                // Retirer de la fin
                _cartesPieces.RemoveAt(_cartesPieces.Count - 1);
                PanneauCartesPieces.Children.Remove(conteneur);

                // Insérer à la position voulue
                _cartesPieces.Insert(positionInsert.Value, carte);
                PanneauCartesPieces.Children.Insert(positionInsert.Value, conteneur);

                // Renumeroter toutes les cartes
                RenumeroterCartes();
            }
            else
            {
                // Appel normal sans position spécifiée : comportement existant
                // (la méthode AjouterCarte originale existe déjà dans CommandesView.cs)
            }
        }

        /// <summary>
        /// Renuméro toutes les cartes après une insertion/suppression.
        /// </summary>
        private void RenumeroterCartes()
        {
            for (int i = 0; i < _cartesPieces.Count; i++)
            {
                var carte = _cartesPieces[i];
                int num = i + 1;

                // Trouver le bandeau en haut de la carte
                if (carte.Conteneur.Child is Border border && border.Child is StackPanel sp && sp.Children.Count > 0)
                {
                    if (sp.Children[0] is Border bandeau && bandeau.Child is Grid bandeauGrid)
                    {
                        // Badge numéro (colonne 0)
                        if (bandeauGrid.Children[0] is Border badgeNum && badgeNum.Child is TextBlock txtBadge)
                        {
                            txtBadge.Text = $"#{num}";
                        }
                        // Titre (colonne 1)
                        if (bandeauGrid.Children[1] is TextBlock lblNum)
                        {
                            lblNum.Text = $"Pièce {num}";
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Recalcule le total de la commande en additionnant toutes les pièces + matériaux.
        /// À appeler après duplication ou modification de prix.
        /// </summary>
        private void RecalculerTotalCommande()
        {
            decimal total = 0;

            foreach (var carte in _cartesPieces)
            {
                // Prix confection
                if (decimal.TryParse(carte.TxtPrix.Text.Trim().Replace(" ", ""), out decimal prixPiece))
                {
                    total += prixPiece;
                }

                // Matériaux
                foreach (var mat in carte.MateriauxBuffer)
                {
                    total += mat.PrixUnitaire * mat.Quantite;
                }
            }

            // Afficher le total (si un contrôle existe dans le XAML - désactivé car TxtTotalCommande n'existe pas dans _Etape5)
            // if (TxtTotalCommande != null)
            // {
            //     TxtTotalCommande.Text = $"{total:N0} FCFA";
            // }
        }

        /// <summary>
        /// Helper : affiche une photo sur une carte.
        /// </summary>
        private void AfficherPhotoSurCarte(Cartepiece carte, string cheminPhoto)
        {
            try
            {
                var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(cheminPhoto, UriKind.Absolute);
                bitmap.EndInit();

                carte.ImgPiece.Source = bitmap;
                carte.TxtPhotoPlaceholderCarte.Visibility = Visibility.Collapsed;
                carte.BtnSupprimerPhotoCarte.Visibility = Visibility.Visible;
                carte.CheminPhoto = cheminPhoto;
            }
            catch
            {
                // Fichier introuvable ou corrompu : ignorer
                carte.CheminPhoto = "";
            }
        }

        /// <summary>
        /// Helper : rafraîchit l'affichage des matériaux d'une carte.
        /// </summary>
        private void RafraichirAffichageMateriauxCarte(Cartepiece carte)
        {
            // Vider la liste actuelle
            carte.PanelListeMateriaux.Children.Clear();

            if (carte.MateriauxBuffer.Count == 0)
            {
                // Message "aucun matériau"
                carte.PanelListeMateriaux.Children.Add(new TextBlock
                {
                    Text = "Aucun matériau ajouté.",
                    FontSize = 11,
                    FontStyle = FontStyles.Italic,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)),
                    Margin = new Thickness(0, 0, 0, 6)
                });
            }
            else
            {
                foreach (var mat in carte.MateriauxBuffer)
                {
                    var ligne = new Border
                    {
                        Background = new SolidColorBrush(Colors.White),
                        BorderBrush = new SolidColorBrush(Color.FromRgb(0xE8, 0xE0, 0xDC)),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(10, 6, 10, 6),
                        Margin = new Thickness(0, 0, 0, 4)
                    };

                    var grid = new Grid();
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    var txtNom = new TextBlock
                    {
                        Text = $"{mat.Designation} (×{mat.Quantite})",
                        FontSize = 11,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    Grid.SetColumn(txtNom, 0);
                    grid.Children.Add(txtNom);

                    decimal sousTotal = mat.PrixUnitaire * mat.Quantite;
                    var txtPrix = new TextBlock
                    {
                        Text = $"{sousTotal:N0} FCFA",
                        FontSize = 11,
                        FontWeight = FontWeights.SemiBold,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(10, 0, 10, 0)
                    };
                    Grid.SetColumn(txtPrix, 1);
                    grid.Children.Add(txtPrix);

                    var btnSuppr = new Button
                    {
                        Content = "🗑️",
                        Width = 24,
                        Height = 24,
                        Background = Brushes.Transparent,
                        BorderThickness = new Thickness(0),
                        Foreground = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26)),
                        Cursor = System.Windows.Input.Cursors.Hand,
                        ToolTip = "Supprimer ce matériau",
                        Tag = mat
                    };
                    btnSuppr.Click += (s, ev) =>
                    {
                        carte.MateriauxBuffer.Remove(mat);
                        RafraichirAffichageMateriauxCarte(carte);
                        RecalculerTotalCommande();
                        DeclenencherSauvegardeBrouillon();
                    };
                    Grid.SetColumn(btnSuppr, 2);
                    grid.Children.Add(btnSuppr);

                    ligne.Child = grid;
                    carte.PanelListeMateriaux.Children.Add(ligne);
                }
            }

            // Mettre à jour le total des matériaux de la carte
            decimal totalMat = carte.MateriauxBuffer.Sum(m => m.PrixUnitaire * m.Quantite);
            carte.TxtTotalMatCarte.Text = $"{totalMat:N0} FCFA";

            // Recalculer le total général
            RecalculerTotalCommande();
        }

        /* ÉTAPE 5 : CreerBoutonIconeSurBandeau transféré vers CommandesView.cs */
    }
}
