namespace AniT.Core.Achievements;

public static class AchievementCatalog
{
    private const string First = "Assets/Badges/primeiros passos/";
    private const string Consistency = "Assets/Badges/consistência/";
    private const string Marathon = "Assets/Badges/maratona/";
    private const string Ratings = "Assets/Badges/avaliacoes/";
    private const string Library = "Assets/Badges/biblioteca/";
    private const string Genres = "Assets/Badges/generos/";
    private const string Time = "Assets/Badges/tempo assistido/";
    private const string Social = "Assets/Badges/social/";
    private const string Legendary = "Assets/Badges/lendarias/";

    public static IReadOnlyList<AchievementDefinition> All { get; } =
    [
        D(1, "FIRST_EPISODE", "Primeiro Passo", "Assistir primeiro episódio.", AchievementCategory.FirstSteps, AchievementRarity.Common, First + "Emblema Primeiro Passo do Espírito TV.png", 1, "episodes_started"),
        D(2, "FIRST_ANIME", "A Jornada Começa", "Iniciar primeiro anime.", AchievementCategory.FirstSteps, AchievementRarity.Common, First + "A Jornada Começa Conquista 02.png", 1, "anime_started"),
        D(3, "FIRST_EPISODE_RATING", "Primeira Opinião", "Avaliar um episódio pela primeira vez.", AchievementCategory.FirstSteps, AchievementRarity.Uncommon, First + "Emblema Primeira Opinião.png", 1, "rated_episodes"),
        D(4, "FIRST_EPISODE_COMPLETED", "Até o Fim", "Concluir primeiro episódio.", AchievementCategory.FirstSteps, AchievementRarity.Uncommon, First + "Emblema 04 Até o Fim.png", 1, "completed_episodes"),
        D(5, "FIRST_LIBRARY_ANIME", "Na Estante", "Adicionar primeiro anime à biblioteca.", AchievementCategory.FirstSteps, AchievementRarity.Uncommon, First + "Conquista 05 Na Estante.png", 1, "library_anime_count"),
        D(6, "FIRST_CUSTOM_LIST", "Minha Primeira Lista", "Criar lista personalizada.", AchievementCategory.FirstSteps, AchievementRarity.Rare, First + "06 Minha Primeira Lista Rara.png", 1, "custom_lists"),
        D(7, "FIRST_FAVORITE", "Esse Eu Gostei", "Favoritar primeiro anime.", AchievementCategory.FirstSteps, AchievementRarity.Rare, First + "Emblema Raro do Primeiro Anime.png", 1, "favorite_anime"),
        D(8, "FIRST_TAG", "Organizando Tudo", "Criar ou usar primeira tag.", AchievementCategory.FirstSteps, AchievementRarity.Rare, First + "Organizando Tudo Conquista Rara.png", 1, "tags_used"),
        D(9, "FIRST_REVIEW", "Deixando Registrado", "Escrever primeira crítica.", AchievementCategory.FirstSteps, AchievementRarity.Epic, First + "Emblema Épico Deixando Registrado.png", 1, "reviews_written"),
        D(10, "FIRST_ANIME_COMPLETED", "Primeira História Concluída", "Finalizar primeiro anime.", AchievementCategory.FirstSteps, AchievementRarity.Epic, First + "Emblema Épico da Primeira História Concluída.png", 1, "completed_anime"),

        D(11, "STREAK_3", "Voltou!", "3 dias consecutivos.", AchievementCategory.Consistency, AchievementRarity.Common, Consistency + "Conquista Voltei Sequência de 3 Dias.png", 3, "longest_streak", "STREAK", "dias"),
        D(12, "STREAK_7", "Na Rotina", "7 dias consecutivos.", AchievementCategory.Consistency, AchievementRarity.Uncommon, Consistency + "Emblema Na Rotina 7 Dias Consecutivos.png", 7, "longest_streak", "STREAK", "dias"),
        D(13, "STREAK_14", "Costume Formado", "14 dias consecutivos.", AchievementCategory.Consistency, AchievementRarity.Rare, Consistency + "Distintivo Azul de 14 Dias Consecutivos.png", 14, "longest_streak", "STREAK", "dias"),
        D(14, "STREAK_30", "Não Parou", "30 dias consecutivos.", AchievementCategory.Consistency, AchievementRarity.Rare, Consistency + "Emblema de Sequência Ininterrupta.png", 30, "longest_streak", "STREAK", "dias"),
        D(15, "STREAK_50", "Sempre Aqui", "50 dias consecutivos.", AchievementCategory.Consistency, AchievementRarity.Epic, Consistency + "Distintivo Sempre Aqui 50 Dias.png", 50, "longest_streak", "STREAK", "dias"),
        D(16, "STREAK_100", "Fidelidade", "100 dias consecutivos.", AchievementCategory.Consistency, AchievementRarity.Epic, Consistency + "Emblema Raro de Fidelidade 100 Dias.png", 100, "longest_streak", "STREAK", "dias"),
        D(17, "STREAK_180", "Modo Hábito", "180 dias consecutivos.", AchievementCategory.Consistency, AchievementRarity.Epic, Consistency + "Emblema Raro Modo Hábito.png", 180, "longest_streak", "STREAK", "dias"),
        D(18, "STREAK_365", "Lenda da Rotina", "365 dias consecutivos.", AchievementCategory.Consistency, AchievementRarity.Legendary, Consistency + "Lenda da Rotina 365 Dias.png", 365, "longest_streak", "STREAK", "dias"),
        D(19, "STREAK_500", "Inabalável", "500 dias consecutivos.", AchievementCategory.Consistency, AchievementRarity.Legendary, Consistency + "Conquista Inabalável 500 Dias.png", 500, "longest_streak", "STREAK", "dias"),
        D(20, "STREAK_1000", "Para Sempre", "1000 dias consecutivos.", AchievementCategory.Consistency, AchievementRarity.Legendary, Consistency + "Emblema Épico de 1.000 Dias.png", 1000, "longest_streak", "STREAK", "dias"),

        D(21, "SESSION_EPISODES_3", "Aquecimento", "3 episódios na mesma sessão.", AchievementCategory.Marathon, AchievementRarity.Common, Marathon + "Aquecimento Maratona de Episódios.png", 3, "session_episode_max", "SESSION_EPISODES", "episódios"),
        D(22, "SESSION_EPISODES_5", "Só Mais Um", "5 episódios na mesma sessão.", AchievementCategory.Marathon, AchievementRarity.Uncommon, Marathon + "Emblema Só Mais Um Maratona de Episódios.png", 5, "session_episode_max", "SESSION_EPISODES", "episódios"),
        D(23, "SESSION_EPISODES_10", "Pegando o Ritmo", "10 episódios na mesma sessão.", AchievementCategory.Marathon, AchievementRarity.Uncommon, Marathon + "Pegando o Ritmo Conquista Incomum.png", 10, "session_episode_max", "SESSION_EPISODES", "episódios"),
        D(24, "SESSION_EPISODES_20", "Sem Pausas", "20 episódios na mesma sessão.", AchievementCategory.Marathon, AchievementRarity.Rare, Marathon + "Conquista Sem Pausas 20.png", 20, "session_episode_max", "SESSION_EPISODES", "episódios"),
        D(25, "DAY_EPISODES_30", "Modo Maratona", "30 episódios no mesmo dia.", AchievementCategory.Marathon, AchievementRarity.Rare, Marathon + "Emblema Modo Maratona Azul.png", 30, "max_daily_episodes", "DAILY_EPISODES", "episódios"),
        D(26, "DAY_EPISODES_50", "Insano", "50 episódios no mesmo dia.", AchievementCategory.Marathon, AchievementRarity.Epic, Marathon + "Emblema Insano Maratona de 50 Episódios.png", 50, "max_daily_episodes", "DAILY_EPISODES", "episódios"),
        D(27, "DAY_WATCH_6H", "Foco Total", "6 horas assistidas no mesmo dia.", AchievementCategory.Marathon, AchievementRarity.Rare, Marathon + "Emblema Foco Total 27 Rara.png", Hours(6), "max_daily_seconds", "DAILY_TIME", "h", AchievementProgressType.DurationSeconds),
        D(28, "DAY_WATCH_12H", "Dia Perdido", "12 horas assistidas no mesmo dia.", AchievementCategory.Marathon, AchievementRarity.Rare, Marathon + "Distintivo Raro Dia Perdido.png", Hours(12), "max_daily_seconds", "DAILY_TIME", "h", AchievementProgressType.DurationSeconds),
        D(29, "DAY_WATCH_18H", "Além dos Limites", "18 horas assistidas no mesmo dia.", AchievementCategory.Marathon, AchievementRarity.Epic, Marathon + "Além dos Limites Conquista Épica.png", Hours(18), "max_daily_seconds", "DAILY_TIME", "h", AchievementProgressType.DurationSeconds),
        D(30, "MARATHON_100H", "Maratonista Supremo", "100 horas acumuladas em sessões de maratona.", AchievementCategory.Marathon, AchievementRarity.Epic, Marathon + "Emblema Épico Maratonista Supremo.png", Hours(100), "marathon_seconds_total", "MARATHON_TIME", "h", AchievementProgressType.DurationSeconds),

        D(31, "FIRST_RATING", "Primeira Nota", "Primeira avaliação.", AchievementCategory.Ratings, AchievementRarity.Common, Ratings + "Emblema Primeira Nota.png", 1, "rated_works"),
        D(32, "RATING_GTE_8", "Gostei!", "Dar uma nota igual ou superior a 8.", AchievementCategory.Ratings, AchievementRarity.Uncommon, Ratings + "Emblema Gostei! Nota 32.png", 1, "ratings_gte_8"),
        D(33, "RATING_4_TO_6", "Sinceridade", "Dar uma nota entre 4 e 6.", AchievementCategory.Ratings, AchievementRarity.Uncommon, Ratings + "Distintivo de Sinceridade 33.png", 1, "ratings_4_to_6"),
        D(34, "RATING_LTE_3", "Sem Passar Pano", "Dar uma nota igual ou inferior a 3.", AchievementCategory.Ratings, AchievementRarity.Uncommon, Ratings + "Selo de Avaliação Sem Passar Pano.png", 1, "ratings_lte_3"),
        D(35, "RATED_WORKS_10", "Crítico em Formação", "Avaliar 10 obras.", AchievementCategory.Ratings, AchievementRarity.Rare, Ratings + "Crítico em Formação Avaliar 10 Obras.png", 10, "rated_works", "RATINGS", "obras"),
        D(36, "FIRST_DETAILED_REVIEW", "Opinião Detalhada", "Escrever primeira resenha.", AchievementCategory.Ratings, AchievementRarity.Rare, Ratings + "Emblema Raro de Opinião Detalhada.png", 1, "reviews_written"),
        D(37, "RATED_WORKS_50", "Avaliador Dedicado", "Avaliar 50 obras.", AchievementCategory.Ratings, AchievementRarity.Rare, Ratings + "Distintivo Raro Avaliador Dedicado.png", 50, "rated_works", "RATINGS", "obras"),
        D(38, "REVIEWS_25", "Resenhista", "Escrever 25 críticas.", AchievementCategory.Ratings, AchievementRarity.Rare, Ratings + "Emblema Raro de Resenhista 38.png", 25, "reviews_written", "REVIEWS", "críticas"),
        D(39, "RATED_WORKS_100", "Referência Crítica", "Avaliar 100 obras.", AchievementCategory.Ratings, AchievementRarity.Epic, Ratings + "Emblema Épico da Referência Crítica.png", 100, "rated_works", "RATINGS", "obras"),
        D(40, "DETAILED_REVIEWS_50", "Voz da Comunidade", "Escrever 50 críticas detalhadas.", AchievementCategory.Ratings, AchievementRarity.Epic, Ratings + "Emblema Épico Voz da Comunidade.png", 50, "detailed_reviews", "REVIEWS", "críticas"),

        D(41, "LIBRARY_5", "Começando a Estante", "Adicionar 5 animes à biblioteca.", AchievementCategory.Library, AchievementRarity.Common, Library + "41.png", 5, "library_anime_count", "LIBRARY", "animes"),
        D(42, "LIBRARY_10", "Pequena Coleção", "Adicionar 10 animes à biblioteca.", AchievementCategory.Library, AchievementRarity.Uncommon, Library + "42.png", 10, "library_anime_count", "LIBRARY", "animes"),
        D(43, "LIBRARY_25", "Colecionador", "Adicionar 25 animes à biblioteca.", AchievementCategory.Library, AchievementRarity.Rare, Library + "43.png", 25, "library_anime_count", "LIBRARY", "animes"),
        D(44, "LIBRARY_50", "Biblioteca Crescendo", "Adicionar 50 animes à biblioteca.", AchievementCategory.Library, AchievementRarity.Rare, Library + "44.png", 50, "library_anime_count", "LIBRARY", "animes"),
        D(45, "LIBRARY_100", "Acervo Pessoal", "Adicionar 100 animes à biblioteca.", AchievementCategory.Library, AchievementRarity.Epic, Library + "45.png", 100, "library_anime_count", "LIBRARY", "animes"),
        D(46, "LIBRARY_250", "Arquivo Otaku", "Adicionar 250 animes à biblioteca.", AchievementCategory.Library, AchievementRarity.Epic, Library + "46.png", 250, "library_anime_count", "LIBRARY", "animes"),
        D(47, "LIBRARY_500", "Bibliotecário", "Adicionar 500 animes à biblioteca.", AchievementCategory.Library, AchievementRarity.Epic, Library + "47.png", 500, "library_anime_count", "LIBRARY", "animes"),
        D(48, "LIBRARY_750", "Guardião das Histórias", "Adicionar 750 animes à biblioteca.", AchievementCategory.Library, AchievementRarity.Legendary, Library + "48.png", 750, "library_anime_count", "LIBRARY", "animes"),
        D(49, "LIBRARY_1000", "Grande Arquivo AniT", "Adicionar 1000 animes à biblioteca.", AchievementCategory.Library, AchievementRarity.Legendary, Library + "49.png", 1000, "library_anime_count", "LIBRARY", "animes"),
        D(50, "LIBRARY_2000", "Biblioteca Infinita", "Adicionar 2000 animes à biblioteca.", AchievementCategory.Library, AchievementRarity.Legendary, Library + "50.png", 2000, "library_anime_count", "LIBRARY", "animes"),

        D(51, "GENRES_3", "Curioso", "Explorar 3 gêneros distintos.", AchievementCategory.Genres, AchievementRarity.Common, Genres + "Emblema Curioso Três Gêneros de Anime.png", 3, "distinct_genres_started", "GENRES", "gêneros"),
        D(52, "GENRES_5", "Explorador", "Explorar 5 gêneros distintos.", AchievementCategory.Genres, AchievementRarity.Uncommon, Genres + "Emblema Explorador AniT 52.png", 5, "distinct_genres_started", "GENRES", "gêneros"),
        D(53, "GENRES_10", "Sem Preconceitos", "Explorar 10 gêneros distintos.", AchievementCategory.Genres, AchievementRarity.Rare, Genres + "Distintivo 53 Sem Preconceitos.png", 10, "distinct_genres_started", "GENRES", "gêneros"),
        D(54, "GENRES_15", "Turista dos Mundos", "Explorar 15 gêneros distintos.", AchievementCategory.Genres, AchievementRarity.Rare, Genres + "Distintivo Turista dos Mundos geschniegelt.png", 15, "distinct_genres_started", "GENRES", "gêneros"),
        D(55, "GENRES_20", "Multigênero", "Explorar 20 gêneros distintos.", AchievementCategory.Genres, AchievementRarity.Epic, Genres + "Conquista Multigênero 55 Gêneros.png", 20, "distinct_genres_started", "GENRES", "gêneros"),
        D(56, "GENRES_25", "Mapa Completo", "Explorar 25 gêneros distintos.", AchievementCategory.Genres, AchievementRarity.Epic, Genres + "Mapa Completo 56 Gêneros.png", 25, "distinct_genres_started", "GENRES", "gêneros"),
        D(57, "COMPLETED_GENRES_15", "Além da Zona de Conforto", "Concluir obras de 15 gêneros diferentes.", AchievementCategory.Genres, AchievementRarity.Rare, Genres + "Além da Zona de Conforto – Distintivo 57.png", 15, "distinct_completed_genres", "COMPLETED_GENRES", "gêneros"),
        D(58, "COMPLETED_GENRES_25", "Mestre dos Gêneros", "Concluir obras de 25 gêneros diferentes.", AchievementCategory.Genres, AchievementRarity.Rare, Genres + "Mestre dos Gêneros Emblema Raro.png", 25, "distinct_completed_genres", "COMPLETED_GENRES", "gêneros"),
        D(59, "GENRE_FIVE_WORKS_20", "Enciclopédia Otaku", "Concluir 5 obras em 20 gêneros.", AchievementCategory.Genres, AchievementRarity.Epic, Genres + "Emblema Épico da Enciclopédia Otaku.png", 20, "genres_with_five_completed", "GENRE_DEPTH", "gêneros"),
        D(60, "ALL_GENRES", "Sem Fronteiras", "Completar todos os gêneros cadastrados.", AchievementCategory.Genres, AchievementRarity.Epic, Genres + "Badge Épica Sem Fronteiras 60.png", 1, "all_genres_completed"),

        D(61, "WATCH_TIME_1H", "Uma Horinha", "Assista 1 hora de anime.", AchievementCategory.WatchTime, AchievementRarity.Common, Time + "Emblema Chibi Uma Horinha.png", Hours(1), "watch_seconds", "WATCH_TIME", "h", AchievementProgressType.DurationSeconds),
        D(62, "WATCH_TIME_5H", "Noite de Anime", "Assista 5 horas de anime.", AchievementCategory.WatchTime, AchievementRarity.Uncommon, Time + "Noite de Anime Conquista 62.png", Hours(5), "watch_seconds", "WATCH_TIME", "h", AchievementProgressType.DurationSeconds),
        D(63, "WATCH_TIME_24H", "Um Dia Inteiro", "Assista 24 horas de anime.", AchievementCategory.WatchTime, AchievementRarity.Rare, Time + "Emblema Um Dia Inteiro 63.png", Hours(24), "watch_seconds", "WATCH_TIME", "h", AchievementProgressType.DurationSeconds),
        D(64, "WATCH_TIME_50H", "Fim de Semana Perdido", "Assista 50 horas de anime.", AchievementCategory.WatchTime, AchievementRarity.Rare, Time + "Emblema Fim de Semana Perdido.png", Hours(50), "watch_seconds", "WATCH_TIME", "h", AchievementProgressType.DurationSeconds),
        D(65, "WATCH_TIME_100H", "Cem Horas de Histórias", "Assista 100 horas de anime.", AchievementCategory.WatchTime, AchievementRarity.Epic, Time + "Distintivo 65 Cem Horas de Histórias.png", Hours(100), "watch_seconds", "WATCH_TIME", "h", AchievementProgressType.DurationSeconds),
        D(66, "WATCH_TIME_250H", "Veterano", "Assista 250 horas de anime.", AchievementCategory.WatchTime, AchievementRarity.Epic, Time + "Medalha Veterano 66 Horas.png", Hours(250), "watch_seconds", "WATCH_TIME", "h", AchievementProgressType.DurationSeconds),
        D(67, "WATCH_TIME_500H", "Relógio Quebrado", "Assista 500 horas de anime.", AchievementCategory.WatchTime, AchievementRarity.Epic, Time + "Conquista Relógio Quebrado 67.png", Hours(500), "watch_seconds", "WATCH_TIME", "h", AchievementProgressType.DurationSeconds),
        D(68, "WATCH_TIME_1000H", "Mil Horas", "Assista 1000 horas de anime.", AchievementCategory.WatchTime, AchievementRarity.Legendary, Time + "Medalha Celestial de Mil Horas.png", Hours(1000), "watch_seconds", "WATCH_TIME", "h", AchievementProgressType.DurationSeconds),
        D(69, "WATCH_TIME_2500H", "Outro Mundo", "Assista 2500 horas de anime.", AchievementCategory.WatchTime, AchievementRarity.Legendary, Time + "Portal Mágico Conquista Épica.png", Hours(2500), "watch_seconds", "WATCH_TIME", "h", AchievementProgressType.DurationSeconds),
        D(70, "WATCH_TIME_5000H", "Uma Vida em Anime", "Assista 5000 horas de anime.", AchievementCategory.WatchTime, AchievementRarity.Legendary, Time + "Distintivo Cósmico Uma Vida em Anime.png", Hours(5000), "watch_seconds", "WATCH_TIME", "h", AchievementProgressType.DurationSeconds),

        D(71, "FIRST_TOP_5", "Meu Primeiro Top", "Criar Top 5.", AchievementCategory.Rankings, AchievementRarity.Common, Time + "Conquista Top 10 com Espírito Flamejante.png", 1, "top5_created"),
        D(72, "TOP_10", "Top 10", "Criar ranking com 10 obras.", AchievementCategory.Rankings, AchievementRarity.Uncommon, Time + "Conquista Top 10 com Espírito Flamejante.png", 10, "ranking_size_max", "RANKING_SIZE", "obras"),
        D(73, "RANKING_CHANGES_10", "Escolhas Difíceis", "Alterar ranking 10 vezes.", AchievementCategory.Rankings, AchievementRarity.Rare, Time + "Emblema Escolhas Difíceis 73.png", 10, "ranking_changes", "RANKING_CHANGES", "alterações"),
        D(74, "FIRST_PERFECT_RATING", "Nota Máxima", "Dar 10 pela primeira vez.", AchievementCategory.Rankings, AchievementRarity.Rare, Time + "Distintivo Nota Máxima 10.10.png", 1, "perfect_ratings"),
        D(75, "PERFECT_RATINGS_5", "Perfeição Rara", "Dar nota 10 para 5 obras.", AchievementCategory.Rankings, AchievementRarity.Rare, Time + "Emblema de Conquista Perfeição Rara.png", 5, "perfect_ratings", "PERFECT_RATINGS", "obras"),
        D(76, "PERFECT_RATINGS_10", "Crème de la Crème", "Dar nota 10 para 10 obras.", AchievementCategory.Rankings, AchievementRarity.Epic, Time + "Distintivo Supremo Crème de la Crème.png", 10, "perfect_ratings", "PERFECT_RATINGS", "obras"),
        D(77, "DEMANDING_JUDGE", "Juiz Exigente", "Avaliar 100 obras sem mais de 10 notas máximas.", AchievementCategory.Rankings, AchievementRarity.Epic, Time + "Distintivo Juiz Exigente 77.png", 1, "demanding_judge"),
        D(78, "STABLE_TOP_10", "Top Definitivo", "Manter o mesmo Top 10 por 180 dias.", AchievementCategory.Rankings, AchievementRarity.Legendary, Time + "Emblema Top 10 Top Definitivo.png", 180, "stable_top10_days", "STABLE_RANKING", "dias"),
        D(79, "RANKINGS_10", "Curador Supremo", "Criar 10 rankings.", AchievementCategory.Rankings, AchievementRarity.Epic, Time + "Distintivo Épico Curador Supremo.png", 10, "rankings_created", "RANKINGS", "rankings"),
        D(80, "TOP_100", "Panteão Pessoal", "Criar um Top 100.", AchievementCategory.Rankings, AchievementRarity.Epic, Time + "Emblema Épico do Panteão Pessoal.png", 100, "ranking_size_max", "RANKING_SIZE", "obras"),

        D(81, "PROFILE_CUSTOMIZED", "Essa Sou Eu", "Personalizar perfil.", AchievementCategory.ProfileBackup, AchievementRarity.Common, Social + "Mascote AniT - Baki-Pi.png", 1, "profile_customized"),
        D(82, "AVATAR_CHANGED", "De Cara Nova", "Alterar avatar ou banner.", AchievementCategory.ProfileBackup, AchievementRarity.Uncommon, Social + "Conquista 82 De Cara Nova.png", 1, "avatar_changed"),
        D(83, "PROFILE_COMPLETE", "Minha Jornada", "Completar perfil.", AchievementCategory.ProfileBackup, AchievementRarity.Rare, Social + "Minha Jornada Perfil Completo.png", 1, "profile_complete"),
        D(84, "FIRST_BACKUP", "Diário Seguro", "Criar primeiro backup.", AchievementCategory.ProfileBackup, AchievementRarity.Uncommon, Social + "Diário Seguro Backup Incomumાન્ય.png", 1, "backups_created", "BACKUPS", "backups"),
        D(85, "BACKUPS_5", "Precavido", "Criar 5 backups.", AchievementCategory.ProfileBackup, AchievementRarity.Rare, Social + "Conquista Precavida Cinco Backups.png", 5, "backups_created", "BACKUPS", "backups"),
        D(86, "BACKUPS_25", "Guardião dos Dados", "Criar 25 backups.", AchievementCategory.ProfileBackup, AchievementRarity.Rare, Social + "Guardião dos Dados Raro.png", 25, "backups_created", "BACKUPS", "backups"),
        D(87, "LIBRARY_IMPORTED", "Nova Casa", "Importar biblioteca em outro computador.", AchievementCategory.ProfileBackup, AchievementRarity.Rare, Social + "Conquista Nova Casa Rara.png", 1, "library_imported"),
        D(88, "BACKUP_RESTORED", "Nada se Perde", "Restaurar backup.", AchievementCategory.ProfileBackup, AchievementRarity.Rare, Social + "Emblema Raro “Nada se Perde”.png", 1, "backups_restored"),
        D(89, "HISTORY_1_YEAR", "Arquivo Eterno", "Manter 1 ano de histórico.", AchievementCategory.ProfileBackup, AchievementRarity.Epic, Social + "Distintivo Arquivo Eterno Épico.png", 365, "history_span_days", "HISTORY", "dias"),
        D(90, "DIARY_2_YEARS", "Minha História AniT", "Manter 2 anos de diário.", AchievementCategory.ProfileBackup, AchievementRarity.Epic, Social + "Emblema Épic  Minha História AniT.png", 730, "history_span_days", "HISTORY", "dias"),

        D(91, "SECRET_0333", "Baki-Pi Está de Olho", "Abrir o AniT exatamente às 03:33.", AchievementCategory.SecretLegendary, AchievementRarity.Secret, Legendary + "Emblema Místico Baki-Pi às 03.33.png", 1, "secret_0333", secret: true),
        D(92, "SECRET_NEXT_10", "Só Mais Um, Eu Prometo", "Usar próximo episódio 10 vezes consecutivas.", AchievementCategory.SecretLegendary, AchievementRarity.Secret, Legendary + "Conquista Secreta Só Mais Um.png", 10, "next_episode_streak", "SECRET_NEXT", "vezes", secret: true),
        D(93, "SECRET_MIDNIGHT_6H", "Quem Precisa Dormir?", "Assistir por 6 horas entre 00:00 e 06:00.", AchievementCategory.SecretLegendary, AchievementRarity.Secret, Legendary + "Emblema Neon Quem Precisa Dormir", Hours(6), "midnight_watch_seconds", "SECRET_NIGHT", "h", AchievementProgressType.DurationSeconds, true),
        D(94, "SECRET_ONE_TO_TEN", "Isso Foi... Questionável", "Alterar a nota de uma mesma obra de 1 para 10.", AchievementCategory.SecretLegendary, AchievementRarity.Secret, Legendary + "Emblema Fantasma De 1 a 10.png", 1, "rating_one_to_ten", secret: true),
        D(95, "SECRET_TEN_TENS", "Baki-Pi Aprova", "Dar nota 10 para 10 animes consecutivos.", AchievementCategory.SecretLegendary, AchievementRarity.Secret, Legendary + "Baki-Pi Aprova Conquista Secreta 95.png", 10, "consecutive_tens", "SECRET_TENS", "notas", secret: true),
        D(96, "LEGENDARY_365_DAYS", "Companheiro de Jornada", "Usar o AniT por 365 dias.", AchievementCategory.SecretLegendary, AchievementRarity.Legendary, Legendary + "Emblema Lendário Companheiro de Jornada.png", 365, "application_usage_days", "APP_DAYS", "dias"),
        D(97, "LEGENDARY_1000_EPISODES", "Guardião das Memórias", "Assistir 1000 episódios.", AchievementCategory.SecretLegendary, AchievementRarity.Legendary, Legendary + "Guardião das Memórias Emblema Lendário.png", 1000, "completed_episodes", "EPISODES", "episódios"),
        D(98, "LEGENDARY_5000_HOURS", "Otaku Além do Tempo", "Assistir 5000 horas.", AchievementCategory.SecretLegendary, AchievementRarity.Legendary, Legendary + "Emblema Lendário Otaku Além do Tempo.png", Hours(5000), "watch_seconds", "WATCH_TIME", "h", AchievementProgressType.DurationSeconds),
        D(99, "MASTER_ANIT", "Mestre do AniT", "Desbloquear 90 conquistas.", AchievementCategory.SecretLegendary, AchievementRarity.Legendary, Legendary + "Emblema Mestre do AniT 99.png", 90, "unlocked_count_excluding_master", "MASTER", "conquistas"),
        D(100, "JOURNEY_NEVER_ENDS", "A Jornada Nunca Termina", "Desbloquear todas as outras 99 conquistas.", AchievementCategory.SecretLegendary, AchievementRarity.SupremeLegendary, Legendary + "Emblema Lendário AniT Jornada Infinita.png", 99, "unlocked_count_excluding_supreme", "MASTER", "conquistas")
    ];

    public static IReadOnlyList<AchievementChain> Chains { get; } = All
        .Where(item => item.ChainCode is not null)
        .GroupBy(item => item.ChainCode!)
        .Select(group => new AchievementChain(group.Key, ChainName(group.Key), group.OrderBy(item => item.ChainOrder).ThenBy(item => item.TargetValue).ToArray()))
        .ToArray();

    public static AchievementDefinition ById(int id) => All.Single(item => item.Id == id);

    private static AchievementDefinition D(
        int id,
        string code,
        string name,
        string description,
        AchievementCategory category,
        AchievementRarity rarity,
        string icon,
        long target,
        string metric,
        string? chain = null,
        string unit = "",
        AchievementProgressType progressType = AchievementProgressType.Counter,
        bool secret = false) =>
        new(id, code, name, description, category, rarity, icon, secret, secret, target > 1, target, metric, progressType, chain, id, unit);

    private static long Hours(long value) => checked(value * 60 * 60);

    private static string ChainName(string code) => code switch
    {
        "STREAK" => "Jornada de consistência",
        "LIBRARY" => "Evolução da biblioteca",
        "GENRES" or "COMPLETED_GENRES" => "Exploração de gêneros",
        "RATINGS" or "REVIEWS" => "Jornada crítica",
        "WATCH_TIME" => "Tempo assistido",
        "BACKUPS" => "Proteção da jornada",
        _ => code.Replace('_', ' ')
    };
}
