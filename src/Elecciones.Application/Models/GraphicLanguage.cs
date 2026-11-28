namespace Elecciones.Application.Models;

public enum GraphicLanguage
{
    Castellano = 0,
    Euskera = 1,
    Catalan = 2,
    Gallego = 3,
    Valenciano = 4
}

public static class GraphicLanguageExtensions
{
    public static string ToDisplayName(this GraphicLanguage language) => language switch
    {
        GraphicLanguage.Castellano => "Castellano",
        GraphicLanguage.Euskera => "Euskera",
        GraphicLanguage.Catalan => "Catalán",
        GraphicLanguage.Gallego => "Gallego",
        GraphicLanguage.Valenciano => "Valenciano",
        _ => "Castellano"
    };

    public static string ToEventName(this GraphicLanguage language) => language switch
    {
        GraphicLanguage.Castellano => "IDIOMA/CASTELLANO",
        GraphicLanguage.Euskera => "IDIOMA/EUSKERA",
        GraphicLanguage.Catalan => "IDIOMA/CATALAN",
        GraphicLanguage.Gallego => "IDIOMA/GALEGO",
        GraphicLanguage.Valenciano => "IDIOMA/VALENCIANO",
        _ => "IDIOMA/CASTELLANO"
    };

    public static GraphicCommand ToGraphicCommand(this GraphicLanguage language) => language switch
    {
        GraphicLanguage.Castellano => GraphicCommand.IdiomaCastellano,
        GraphicLanguage.Euskera => GraphicCommand.IdiomaEuskera,
        GraphicLanguage.Catalan => GraphicCommand.IdiomaCatalan,
        GraphicLanguage.Gallego => GraphicCommand.IdiomaGallego,
        GraphicLanguage.Valenciano => GraphicCommand.IdiomaValenciano,
        _ => GraphicCommand.IdiomaCastellano
    };

    public static bool TryFromCommand(GraphicCommand command, out GraphicLanguage language)
    {
        switch (command)
        {
            case GraphicCommand.IdiomaCastellano:
                language = GraphicLanguage.Castellano;
                return true;
            case GraphicCommand.IdiomaEuskera:
                language = GraphicLanguage.Euskera;
                return true;
            case GraphicCommand.IdiomaCatalan:
                language = GraphicLanguage.Catalan;
                return true;
            case GraphicCommand.IdiomaGallego:
                language = GraphicLanguage.Gallego;
                return true;
            case GraphicCommand.IdiomaValenciano:
                language = GraphicLanguage.Valenciano;
                return true;
            default:
                language = GraphicLanguage.Castellano;
                return false;
        }
    }
}
