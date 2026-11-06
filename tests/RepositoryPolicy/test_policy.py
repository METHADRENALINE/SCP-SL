import importlib.util
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('policy', ROOT / 'tools/RepositoryPolicy/check.py')
policy = importlib.util.module_from_spec(spec)
spec.loader.exec_module(policy)


class PublicationPolicyTests(unittest.TestCase):
    def test_only_reviewed_map_assets_are_allowed(self):
        for name in policy.MAP_ASSETS:
            data = (ROOT / name).read_bytes()
            with self.subTest(name=name):
                self.assertEqual([], policy.problems(name, data))
                self.assertTrue(policy.problems(name, data + b'private'))
        self.assertTrue(policy.problems('src/Methadrenaline.Server/Map/unknown.deflate', b'private'))

    def test_all_reviewed_templates_pass(self):
        for name in policy.CONFIG_FILES:
            with self.subTest(name=name):
                self.assertEqual([], policy.problems(name, (ROOT / name).read_bytes()))

    def test_runtime_files_are_rejected_even_when_forced_into_git(self):
        for name in ['configs/game/UserIDWhitelist.txt', 'configs/game/ScpTickets.txt',
                     'configs/plugins/preferences.json', 'deployment/player-names.yml',
                     'deployment/status.json', 'logs/round.txt', 'data/votes.json',
                     'backups/server.zip', 'src/Methadrenaline.Server/Audio/music.ogg',
                     'deployment/.env', 'configs/game/../players.json']:
            with self.subTest(name=name):
                self.assertTrue(policy.problems(name, b'private runtime content'))

    def test_unknown_assignments_are_rejected_without_relying_on_nickname_detection(self):
        name = 'configs/game/config_remoteadmin.txt'
        text = (ROOT / name).read_text()
        self.assertTrue(policy.problems(name, text.replace('Members:\n', 'Members:\n - someone: owner\n').encode()))
        self.assertTrue(policy.problems(name, text.replace('owner_badge: Owner', 'owner_badge: Someone').encode()))
        self.assertTrue(policy.problems(name, (text + '\ncustom_badge: someone\n').encode()))

    def test_player_rank_mapping_is_rejected(self):
        name = 'configs/plugins/InventoryControl/config.yml'
        self.assertTrue(policy.problems(name, (ROOT / name).read_text().replace('inventory_rank: {}', 'inventory_rank:\n  someone: owner').encode()))

    def test_private_voting_and_account_exceptions_are_rejected(self):
        name = 'configs/plugins/METHADRENALINE/config.yml'
        text = (ROOT / name).read_text()
        for old, new in [('basic_asn_exempt_user_ids: []', 'basic_asn_exempt_user_ids: [someone]'),
                         ('mode_voting_enabled: false', 'mode_voting_enabled: true'),
                         ("mode_voting_token_file: ''", 'mode_voting_token_file: /private/token')]:
            self.assertTrue(policy.problems(name, text.replace(old, new).encode()))

    def test_duplicate_sensitive_fields_are_rejected(self):
        name = 'configs/game/config_remoteadmin.txt'
        text = (ROOT / name).read_text() + '\nMembers:\n - someone: owner\n'
        self.assertTrue(policy.problems(name, text.encode()))

    def test_nested_data_cannot_hide_below_an_empty_collection(self):
        name = 'configs/plugins/METHADRENALINE/config.yml'
        text = (ROOT / name).read_text().replace('basic_asn_exempt_user_ids: []', 'basic_asn_exempt_user_ids: []\n  - someone')
        self.assertTrue(policy.problems(name, text.encode()))

    def test_personal_identifiers_and_secrets_are_rejected(self):
        steam = '765611' + '9' * 11
        discord = '9' * 18
        for sample in [steam, discord, 'test.person' + '@' + 'example.org', 'ghp_' + 'x' * 30,
                       'github_pat_' + 'x' * 30, '192.168.' + '44.90', bytes([0, 1, 2])]:
            data = sample if isinstance(sample, bytes) else sample.encode()
            self.assertTrue(policy.problems('src/example.cs', data))

    def test_generic_game_configuration_is_required(self):
        name = 'configs/game/config_gameplay.txt'
        text = (ROOT / name).read_text()
        for old, new in [('query_administrator_password: none', 'query_administrator_password: hidden'),
                         ('contact_email: default', 'contact_email: private'),
                         ('server_ip: auto', 'server_ip: server.example.org')]:
            self.assertTrue(policy.problems(name, text.replace(old, new).encode()))


if __name__ == '__main__':
    unittest.main()
