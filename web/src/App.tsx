import { PlaceOrderForm } from "./components/PlaceOrderForm";
import { OrderLookup } from "./components/OrderLookup";
import "./App.css";

function App() {
  return (
    <>
      <h1>Saga Inventory Engine</h1>
      <PlaceOrderForm />
      <OrderLookup />
    </>
  );
}

export default App;
