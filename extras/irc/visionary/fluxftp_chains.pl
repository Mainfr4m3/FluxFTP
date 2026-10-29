# Visionary live-slot helper v2.1. Copy to Visionary/custom/fluxftp_chains.pl.
# @args and $return_sig are supplied by Visionary's /custom command.
my ($section, $release, $tick, @pairs) = @args;
my @packed;
eval {
    die "invalid chain arguments" unless defined($tick) && $tick =~ /^\d+$/ && @pairs;
    die "invalid section or release" if !defined($section) || !defined($release) || "$section$release" =~ /[\s\x00-\x1f]/;
    # Read the live database, never the exported INI snapshots. Read-only connection.
    my $db = DBI->connect('dbi:SQLite:dbname=User_Files/sites.db', '', '',
        { RaiseError => 1, PrintError => 0, sqlite_open_flags => 1 });
    # Read this section once, rather than one query per site in every chain.
    my $query = $db->prepare('SELECT sitename, slotsdn, slotsup, sectslotsdn, sectslotsup FROM sections WHERE section = ? COLLATE NOCASE');
    my %cache;
    $query->execute($section);
    while (my @record = $query->fetchrow_array) {
        my ($site, @row) = @record;
        next unless defined($site);
        my $key = lc($site);
        next if exists $cache{$key};
        my @values = ('-', '-');
        for my $i (0, 1) {
            # Same precedence as Visionary Chains.pm: nonzero section override, then site value.
            my $value = $row[$i + 2] || $row[$i];
            $values[$i] = $value if defined($value) && $value =~ /^\d+$/ && $value <= 1024;
        }
        $cache{$key} = \@values;
    }
    my $limits = sub { $cache{lc($_[0])} || ['-', '-'] };
    for my $pair (@pairs) {
        die "invalid site pair" unless $pair =~ /^([^;\s,\/\\]+);([^;\s,\/\\]+)$/;
        my ($source, $target) = ($1, $2);
        push @packed, join(';', $source, $target, $limits->($source)->[0], $limits->($target)->[1]);
    }
    $query->finish;
    $db->disconnect;
    1;
} or do {
    my $error = $@ || 'unknown database error';
    $error =~ s/[\r\n]/ /g;
    my $context = defined($tick) && $tick =~ /^\d+$/ && defined($section) && defined($release)
        && "$section$release" !~ /[\s\x00-\x1f]/ ? "$section $release $tick " : '';
    &::send_2_mirc("!SIGNAL $return_sig ERROR $context$error\n");
    return undef;
};
&::send_2_mirc("!SIGNAL $return_sig $section $release $tick " . join(',', @packed) . "\n");
return undef;
