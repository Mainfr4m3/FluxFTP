use strict;
use warnings;
our (@args, $return_sig, @messages, %rows, $fail);
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
    sub prepare { bless {}, 'MockQuery' }
    sub disconnect { 1 }
    package MockQuery;
    sub execute { $_[0]->{key} = "$_[1]/$_[2]"; }
    sub fetchrow_array { @{ $main::rows{$_[0]->{key}} || [] } }
    sub finish { 1 }
}
sub run {
    @args = ('TV', 'Release-GROUP', 123, @_);
    @messages = ();
    { no strict; do './extras/irc/visionary/fluxftp_chains.pl'; die $@ if $@; }
    die 'missing module response' unless @messages == 1;
    return $messages[0];
}
$rows{'A/TV'} = [8, 3, 2, 0];
$rows{'B/TV'} = [4, 6, 0, 1];
die 'section overrides or direction mapping wrong' unless run('A;B') =~ /A;B;2;1\n$/;
$rows{'A/TV'} = [9, 3, 0, 0];
$rows{'B/TV'} = [4, 0, 0, 0];
die 'live changes or zero not retained' unless run('A;B') =~ /A;B;9;0\n$/;
die 'missing values not marked unknown' unless run('A;UNKNOWN') =~ /A;UNKNOWN;9;-\n$/;
die 'invalid chain not rejected' unless run('A;B', 'INVALID') =~ /ERROR/;
$fail = 1;
die 'database error not reported' unless run('A;B') =~ /ERROR.*database unavailable/;
print "PASS: live database reads, section precedence, zero, missing values and explicit failures.\n";
