import * as d3 from "d3";

export default class VizBackground {

    private svg;

    constructor(_selector) {

        const svg = d3.select(_selector);
        svg.attr('background-color', 'white');
        svg.selectAll('*').remove();
        const margin = {
            top: 0,
            right: 0,
            bottom: 0,
            left: 0
        };
        const width = +svg.attr('width') - margin.left - margin.right;
        const height = +svg.attr('height') - margin.top - margin.bottom;

        this.svg = svg;
    }

    public visualize(data) {

        // content area of your visualization
        const vis = this.svg.append('g');
        vis.append('rect');

        let scale = d3.scaleLinear()
            .range([20, 90])
            .domain([-256, 0]);
        let hueScale = d3.scaleLinear()
            .range([250, 200])
            .domain([-120, -50]);

        let s = this.reduceArray(data.slice(0, 3));
        let h = this.reduceArray(data.slice(0, Math.floor(data.length / 3)));
        let l = this.reduceArray(data.slice(data));

        vis.select("rect")
            .attr("fill", "hsl(" + (hueScale(h)) + ", "
                + scale(s) + "%,"
                + scale(l) + "%)");

        this.debouncer(this.lightModeDebouncer(this.svg, hueScale(h), scale(s), scale(l)), 500);

        vis.select("rect")
            .attr("width", vis.attr("width"))
            .attr("height", vis.attr("height"));
    }

    
    lightModeDebouncer(svg, h, s, l){
            if (l > 50) {
                return svg.attr("class", "light");
            } else {
                return svg.attr("class", "dark");
            }
        }

    reduceArray(d) {
        return d.reduce(function(a, x) {
            return a + x;
        }, 0) / d.length;
    }

    debouncer(callback, intervalSize) {
        var timeout;

        return function() {
            if (timeout) {
                return;
            }
            timeout = window.setTimeout(function() {
                timeout = null;
            }, intervalSize);
            callback.apply(this, arguments);
        }
    }
}
