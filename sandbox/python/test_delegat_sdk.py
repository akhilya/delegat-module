import unittest
import re
from pathlib import Path
from unittest.mock import Mock

from delegat_sdk import DelegatClient


class CabinetClientContractTests(unittest.TestCase):
    def test_model_contract_names_match_actual_public_sdk_methods(self):
        contract = (Path(__file__).resolve().parents[2] / "backend/Services/Ai/Agent/PythonSdkContract.cs").read_text(encoding="utf-8")
        documented = set(re.findall(r"client\.(\w+)\(", contract))
        actual = {name for name in dir(DelegatClient) if not name.startswith("_") and callable(getattr(DelegatClient, name))}
        self.assertEqual(actual, documented)

    def test_invented_get_request_fallback_fails_before_any_http_request(self):
        client = DelegatClient()
        client._req = Mock()
        with self.assertRaisesRegex(AttributeError, "request"):
            try:
                client.get("/products")
            except AttributeError:
                client.request("GET", "/products")
        client._req.assert_not_called()

    def test_platform_returns_the_cabinet_pagination_contract_and_sql_returns_rows(self):
        client = DelegatClient()
        client._req = Mock(return_value={"items": [{"id": 1, "name": "Sample"}], "totalCount": 1, "lastPage": True})
        self.assertTrue(client.platform("GET", "/products?startIndex=0&pageSize=100")["lastPage"])
        client._req.assert_called_once_with("POST", "/platform", {"method": "GET", "path": "/products?startIndex=0&pageSize=100"})
        client._req.reset_mock()
        client._req.return_value = {"columns": ["id"], "rows": [{"id": 1}]}
        self.assertEqual([{"id": 1}], client.sql("SELECT id FROM products LIMIT 100")["rows"])
        client._req.assert_called_once_with("POST", "/sql", {"sql": "SELECT id FROM products LIMIT 100"})

    def test_rule_search_uses_complete_pages_exact_cyrillic_letter_and_explicit_consonants(self):
        names = ["Миниатюра Магнето / Люди Икс", "Набор миниатюр Эльфы - Властелин колец", "Миниатюра Леон", "aаБвгд"]
        client = DelegatClient()
        client._req = Mock(side_effect=[
            {"items": [{"id": 1, "name": names[0]}, {"id": 2, "name": names[1]}], "totalCount": 4, "lastPage": False},
            {"items": [{"id": 3, "name": names[2]}, {"id": 4, "name": names[3]}], "totalCount": 4, "lastPage": True},
        ])
        products, start = [], 0
        while True:
            data = client.platform("GET", f"/products?startIndex={start}&pageSize=100")
            products.extend(data["items"])
            start += len(data["items"])
            if data["lastPage"]:
                break
        consonants = set("бвгджзйклмнпрстфхцчшщbcdfghjklmnpqrstvwxyz")
        matches = [p["id"] for p in products if p["name"].lower().count("а") == 2 and sum(ch in consonants for ch in p["name"].lower()) > 3]
        self.assertEqual([3], matches)
        self.assertEqual(2, client._req.call_count)


class ShopRequestTests(unittest.TestCase):
    def test_shop_request_calls_read_only_agent_route(self):
        client = DelegatClient()
        client._req = Mock(return_value={"ok": True, "status": 200, "body": "{}"})

        result = client.shop_request(17, "seller", "/v3/product/list?limit=10")

        self.assertEqual(result["status"], 200)
        client._req.assert_called_once_with(
            "POST",
            "/marketplace/17/read",
            {"host": "seller", "path": "/v3/product/list?limit=10"},
        )

    def test_shop_request_rejects_urls_and_non_positive_connection_ids(self):
        client = DelegatClient()
        client._req = Mock()

        for connection_id, path in [
            (0, "/products"),
            (-2, "/products"),
            (True, "/products"),
            (1, "https://attacker.example/products"),
            (1, "//attacker.example/products"),
            (1, "/products#fragment"),
        ]:
            with self.subTest(connection_id=connection_id, path=path):
                with self.assertRaises(ValueError):
                    client.shop_request(connection_id, "seller", path)

        client._req.assert_not_called()


class ShopReadTests(unittest.TestCase):
    def test_shop_read_calls_named_read_operation_route(self):
        client = DelegatClient()
        client._req = Mock(return_value={"ok": True, "status": 200, "body": "{}"})

        result = client.shop_read(17, "product_list", {"limit": 25, "cursor": "abc"})

        self.assertEqual(result["status"], 200)
        client._req.assert_called_once_with(
            "POST",
            "/marketplace/17/read-operation",
            {"operation": "product_list", "arguments": {"limit": 25, "cursor": "abc"}},
        )

    def test_shop_read_rejects_bad_ids_operations_and_arguments_locally(self):
        client = DelegatClient()
        client._req = Mock()

        for connection_id, operation, arguments in [
            (0, "product_list", {}),
            (True, "product_list", {}),
            (1, "POST /v3/product/import", {}),
            (1, "Product_List", {}),
            (1, "product_list", []),
            (1, "product_list", {"payload": "x" * 17_000}),
        ]:
            with self.subTest(connection_id=connection_id, operation=operation):
                with self.assertRaises(ValueError):
                    client.shop_read(connection_id, operation, arguments)

        client._req.assert_not_called()

    def test_shop_operations_lists_adapter_read_contract(self):
        client = DelegatClient()
        operations = {"operations": [{"name": "product_cards", "argumentsSchemaJson": "{}"}]}
        client._req = Mock(return_value=operations)

        result = client.shop_operations(17)
        self.assertEqual({}, result["operations"][0]["argumentsSchema"])
        self.assertEqual("{}", result["operations"][0]["argumentsSchemaJson"])
        client._req.assert_called_once_with("GET", "/marketplace/17/read-operations")

    def test_shop_operations_rejects_bad_connection_id(self):
        client = DelegatClient()
        client._req = Mock()

        for connection_id in (0, True, "17"):
            with self.subTest(connection_id=connection_id):
                with self.assertRaises(ValueError):
                    client.shop_operations(connection_id)
        client._req.assert_not_called()


if __name__ == "__main__":
    unittest.main()
