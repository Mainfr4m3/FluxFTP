use strict;
use warnings;
our (@args, $return_sig, @messages, %rows, $fail, $queries);
$return_sig = 'FLUXFTP_LIVE_CHAINS';
sub send_2_mirc { push @messages, $_[0]; }
{
    package DBI;
    sub connect {
        die 'database unavailable' if $main::fail;
        die 'database must be read-only' unless $_[4]->{sqlite_open_flags} == 1;
        return bless {}, 'MockDb';
    }
    package MockDb;
    sub prepare {
        die 'expected one bulk section query' unless $_[1] =~ /SELECT sitename,.*WHERE section = \?/;
        bless {}, 'MockQuery';
    }
    sub disconnect { 1 }
    package MockQuery;
    sub execute {
        my ($self, $section) = @_;
        $main::queries++;
        $self->{rows} = [ map { my ($site) = split '/', $_; [$site, @{$main::rows{$_}}] }
            grep { my (undef, $name) = split '/', $_; lc($name) eq lc($section) } sort keys %main::rows ];
    }
    sub fetchrow_array { my $row = shift @{$_[0]->{rows}}; return $row ? @$row : (); }
    sub finish { 1 }
}
sub run {
    @args = ('TV', 'Release-GROUP', 123, @_);
    @messages = ();
    $queries = 0;
    { no strict; do './extras/irc/visionary/fluxftp_chains.pl'; die $@ if $@; }
    die 'missing module response' unless @messages == 1;
    return $messages[0];
}
$rows{'A/TV'} = [8, 3, 2, 0];
$rows{'B/TV'} = [4, 6, 0, 1];
die 'section overrides or direction mapping wrong' unless run('A;B') =~ /A;B;2;1\n$/;
die 'query count must not grow with chain length' unless run(map { 'A;B' } 1..25) =~ /A;B;2;1/ && $queries == 1;
$rows{'A/TV'} = [9, 3, 0, 0];
$rows{'B/TV'} = [4, 0, 0, 0];
die 'live changes or zero not retained' unless run('A;B') =~ /A;B;9;0\n$/;
die 'missing values not marked unknown' unless run('A;UNKNOWN') =~ /A;UNKNOWN;9;-\n$/;
die 'case-insensitive site matching lost' unless run('a;b') =~ /a;b;9;0\n$/;

$return_sig = 'FLUXFTP_LIVE_PROBE';
die 'diagnostic probe must use its isolated callback' unless run('A;B') =~ /^!SIGNAL FLUXFTP_LIVE_PROBE TV Release-GROUP 123 A;B;9;0\n$/;
$return_sig = 'FLUXFTP_LIVE_CHAINS';
die 'invalid chain not rejected' unless run('A;B', 'INVALID') =~ /ERROR/;
$fail = 1;
die 'database error lost request correlation' unless run('A;B') =~ /ERROR TV Release-GROUP 123 .*database unavailable/;
print "PASS: one bulk read per section request, live changes, case folding, section precedence, zero, missing values and correlated failures.\n";
