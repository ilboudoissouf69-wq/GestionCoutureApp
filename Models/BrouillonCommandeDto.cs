using System;
using System.Collections.Generic;

namespace GestionCoutureApp.Models
{
    /// <summary>
    /// DTO pour sérialiser/désérialiser le contenu JSON du brouillon.
    /// </summary>
    public class BrouillonCommandeDto
    {
        public int? IdClient { get; set; }
        public string? NomClient { get; set; }
        public decimal Acompte { get; set; }
        public DateTime? DateFin { get; set; }
        public TimeSpan? HeureFin { get; set; }
        public string? Notes { get; set; }
        
        public List<BrouillonPieceDto> Pieces { get; set; } = new();
    }

    /// <summary>
    /// DTO pour une pièce dans le brouillon.
    /// </summary>
    public class BrouillonPieceDto
    {
        public int? IdTypeVetement { get; set; }
        public string? TypeVetement { get; set; }
        public int? IdCouturier { get; set; }
        public decimal Prix { get; set; }
        public string? Description { get; set; }
        public string? CheminPhoto { get; set; }  // Chemin uniquement, PAS de base64
        
        public List<BrouillonMesureDto> Mesures { get; set; } = new();
        public List<BrouillonMaterielDto> Materiaux { get; set; } = new();
    }

    /// <summary>
    /// DTO pour une mesure dans le brouillon.
    /// </summary>
    public class BrouillonMesureDto
    {
        public string? Nom { get; set; }
        public string? Valeur { get; set; }
    }

    /// <summary>
    /// DTO pour un matériau dans le brouillon.
    /// </summary>
    public class BrouillonMaterielDto
    {
        public string? Nom { get; set; }
        public decimal Prix { get; set; }
        public int Quantite { get; set; }
    }
}
