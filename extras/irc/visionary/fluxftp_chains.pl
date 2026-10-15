# Visionary custom module. Copy to Visionary/custom/fluxftp_chains.pl.
# @args and $return_sig are supplied by Visionary's /custom command.
my ($section, $release, $tick, @pairs) = @args;
my @packed;
eval {
    die "invalid chain arguments" unless defined($tick) && $tick =~ /^\d+$/ && @pairs;
    die "invalid section or release" if !defined($section) || !defined($release) || "$section$release" =~ /[\s\x00-\x1f]/;
    # Read the live database, never the exported INI snapshots. Read-only connection.
    my $db = DBI->connect('dbi:SQLite:dbname=User_Files/sites.db', '', '',
        { RaiseError => 1, PrintError => 0, sqlite_open_flags => 1 });
    my $query = $db->prepare('SELECT slotsdn, slotsup, sectslotsdn, sectslotsup FROM sections WHERE sitename = ? COLLATE NOCASE AND section = ? COLLATE NOCASE');
    my %cache;
    my $limits = sub {
        my ($site) = @_;
        return $cache{$site} if exists $cache{$site};
        $query->execute($site, $section);
        my @row = $query->fetchrow_array;
        my @values = ('-', '-');
        for my $i (0, 1) {
            # Same precedence as Visionary Chains.pm: nonzero section override, then site value.
            my $value = $row[$i + 2] || $row[$i];
            $values[$i] = $value if defined($value) && $value =~ /^\d+$/ && $value <= 1024;
        }
        return $cache{$site} = \@values;
    };
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
    &::send_2_mirc("!SIGNAL $return_sig ERROR $error\n");
    return undef;
};
&::send_2_mirc("!SIGNAL $return_sig $section $release $tick " . join(',', @packed) . "\n");
return undef;
